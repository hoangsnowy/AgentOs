// Composition root for the API host. All wiring is owned by the modules — this file is just an
// assembly list + ASP.NET Core lifecycle: AddServiceDefaults, AddModulesFromAssemblies, then
// MapModuleEndpoints + a couple of host-level health/meta routes.

using AgentOs.Api.Mcp;
using AgentOs.Domain.Llm;
using AgentOs.Modules.AppConfig;
using AgentOs.Modules.Identity;
using AgentOs.Modules.Identity.Auth;
using AgentOs.Modules.Integration;
using AgentOs.Modules.Llm;
using AgentOs.Modules.Mcp;
using AgentOs.Modules.Pipeline;
using AgentOs.Modules.RemoteAgent;
using AgentOs.Modules.Sessions;
using AgentOs.Modules.Tenants;
using AgentOs.Modules.Tools;
using AgentOs.Modules.Workspaces;
using AgentOs.ServiceDefaults;
using AgentOs.SharedKernel.Modularity;
using AgentOs.SharedKernel.Plugins;
using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Logging.AddSimpleConsole(options =>
{
    options.IncludeScopes = true;
    options.SingleLine = false;
    options.TimestampFormat = "HH:mm:ss.fff ";
});

builder.Services.AddOpenApi();
builder.Services.AddDataProtection();

// S2 — per-tenant request rate limiting. An in-flight throttle on the request pipeline, INDEPENDENT
// of the month-to-date BudgetGuard (which is post-hoc and cannot bound a burst). Partitioned by the
// tenant claim so one tenant's spike can't starve another; a token bucket smooths bursts. Health +
// service-identity routes are exempt so liveness/readiness probes are never throttled.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var path = httpContext.Request.Path;
        if (path.StartsWithSegments("/health") || path.StartsWithSegments("/alive") || path.Value == "/")
        {
            return RateLimitPartition.GetNoLimiter("exempt");
        }

        var tenant = httpContext.User.FindFirst("tenant")?.Value
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";

        return RateLimitPartition.GetTokenBucketLimiter(tenant, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 120,
            TokensPerPeriod = 120,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    });
});

// Response compression — Brotli + Gzip for REST/JSON + Scalar responses. The default compressible
// MIME set excludes text/event-stream, so the streaming MCP endpoint (/mcp) is left uncompressed
// and un-buffered. Fastest level keeps CPU overhead low.
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    o.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
});
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProviderOptions>(
    o => o.Level = System.IO.Compression.CompressionLevel.Fastest);
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProviderOptions>(
    o => o.Level = System.IO.Compression.CompressionLevel.Fastest);
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddApplicationInsightsTelemetry();
}

// Auth scheme is host-specific: API hosts Keycloak JWT bearer (Web does cookie + OIDC).
builder.Services.AddJwtAuth(builder.Configuration);

// Module discovery: each .Assembly contributes one IModule.
builder.Services.AddModulesFromAssemblies(builder.Configuration,
    typeof(AppConfigModule).Assembly,
    typeof(LlmModule).Assembly,
    typeof(IdentityModule).Assembly,
    typeof(TenantsModule).Assembly,
    typeof(PipelineModule).Assembly,
    typeof(ToolsModule).Assembly,
    typeof(IntegrationModule).Assembly,
    typeof(WorkspacesModule).Assembly,
    typeof(SessionsModule).Assembly,
    typeof(McpModule).Assembly,
    typeof(RemoteAgentModule).Assembly);

// Runtime plugins: discover IAgentOsPlugin assemblies dropped in the plugins folder (Plugins:Path,
// default "plugins" under the content root). A missing folder is a no-op.
builder.Services.AddPlugins(builder.Configuration,
    System.IO.Path.Combine(builder.Environment.ContentRootPath, builder.Configuration["Plugins:Path"] ?? "plugins"));

// Epic E4 — expose AgentOs pipeline as MCP server. WithToolsFromAssembly discovers every
// [McpServerToolType]-attributed class in the Api assembly (PipelineMcpTools today, more later).
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly(typeof(PipelineMcpTools).Assembly);

var app = builder.Build();

await app.Services.InitializeModulesAsync();

app.UseResponseCompression();

app.UseAuthentication();
app.UseAuthorization();

// After auth so the per-tenant partition can read the tenant claim (S2).
app.UseRateLimiter();

if (!app.Environment.IsProduction())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("AgentOS API")
               .WithTheme(ScalarTheme.BluePlanet)
               .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });
}

app.MapGet("/", () => Results.Ok(new
{
    name = "agentos",
    version = "0.5.0-modular",
    status = "pipeline-ready"
}))
   .WithName("Root")
   .WithSummary("Service identity")
   .WithTags("Meta");

app.MapGet("/health", (IOptions<LlmOptions> llm) =>
{
    var o = llm.Value;
    var claudeReady = !string.IsNullOrWhiteSpace(o.Claude.ApiKey);
    var azureReady = !string.IsNullOrWhiteSpace(o.AzureOpenAi.ApiKey) && !string.IsNullOrWhiteSpace(o.AzureOpenAi.Endpoint);
    return Results.Ok(new
    {
        status = "Healthy",
        utc = DateTime.UtcNow,
        llm = new
        {
            provider = o.Provider,
            forceProvider = o.ForceProvider,
            claudeKeyConfigured = claudeReady,
            azureKeyConfigured = azureReady,
        },
    });
})
   .WithName("Health")
   .WithSummary("Liveness probe + LLM provider readiness")
   .WithTags("Meta");

app.MapModuleEndpoints();

// Epic E4 — MCP HTTP endpoint at /mcp. Streamable HTTP per spec; authenticated when the JWT
// middleware above is active.
app.MapMcp("/mcp");

// Settings "Test connection" — probe the configured provider with a minimal call.
app.MapPost("/llm/test", async (ILlmClientFactory factory, IConfiguration cfg, CancellationToken ct) =>
{
    var provider = cfg["Agents:Orchestrator:Provider"] ?? "Anthropic";
    var model = cfg["Agents:Orchestrator:Model"] ?? "claude-haiku-4-5";
    try
    {
        var client = factory.Create(provider);
        var probe = new LlmRequest("You are a connectivity probe.", "Reply with the single word: OK", model, 0.0, 5);
        var resp = await client.SendAsync(probe, ct).ConfigureAwait(false);
        return Results.Ok(new { ok = true, provider = client.Provider, model, sample = resp.Content?.Trim() });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { ok = false, provider, model, error = ex.Message });
    }
})
   .WithName("LlmTest")
   .WithSummary("Probe the configured LLM provider with a minimal call")
   .WithTags("Settings")
   .RequireAuthorization();

app.Run();
