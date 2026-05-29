// Composition root for the API host. All wiring is owned by the modules — this file is just an
// assembly list + ASP.NET Core lifecycle: AddServiceDefaults, AddModulesFromAssemblies, then
// MapModuleEndpoints + a couple of host-level health/meta routes.

using AgenticSdlc.Domain.Llm;
using AgenticSdlc.Modules.AppConfig;
using AgenticSdlc.Modules.Identity;
using AgenticSdlc.Modules.Integration;
using AgenticSdlc.Modules.Llm;
using AgenticSdlc.Modules.Pipeline;
using AgenticSdlc.Modules.RemoteAgent;
using AgenticSdlc.Modules.Tenants;
using AgenticSdlc.ServiceDefaults;
using AgenticSdlc.SharedKernel.Modularity;
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
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddApplicationInsightsTelemetry();
}

// Module discovery: each .Assembly contributes one IModule.
builder.Services.AddModulesFromAssemblies(builder.Configuration,
    typeof(AppConfigModule).Assembly,
    typeof(LlmModule).Assembly,
    typeof(IdentityModule).Assembly,
    typeof(TenantsModule).Assembly,
    typeof(PipelineModule).Assembly,
    typeof(IntegrationModule).Assembly,
    typeof(RemoteAgentModule).Assembly);

var app = builder.Build();

await app.Services.InitializeModulesAsync();

app.UseAuthentication();
app.UseAuthorization();

if (!app.Environment.IsProduction())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("Agentic SDLC API")
               .WithTheme(ScalarTheme.BluePlanet)
               .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });
}

app.MapGet("/", () => Results.Ok(new
{
    name = "agentic-sdlc-net",
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
