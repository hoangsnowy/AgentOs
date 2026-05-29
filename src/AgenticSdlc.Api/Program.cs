// AgenticSdlc.Api/Program.cs
// Phase 4 — Compose: LLM Gateway + Agents + Pipeline endpoints + Scalar UI.

using AgenticSdlc.Api.Auth;
using AgenticSdlc.Api.Endpoints;
using AgenticSdlc.Application.Configuration;
using AgenticSdlc.Domain.Llm;
using AgenticSdlc.Infrastructure.Agents;
using AgenticSdlc.Infrastructure.Configuration;
using AgenticSdlc.Infrastructure.Llm;
using AgenticSdlc.Infrastructure.Metrics;
using AgenticSdlc.Infrastructure.Persistence;
using AgenticSdlc.Infrastructure.Pipeline;
using AgenticSdlc.Infrastructure.Validation;
using AgenticSdlc.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Aspire service defaults: OpenTelemetry, health checks, service discovery, HTTP resilience.
builder.AddServiceDefaults();

// Logging
builder.Logging.AddSimpleConsole(options =>
{
    options.IncludeScopes = true;
    options.SingleLine = false;
    options.TimestampFormat = "HH:mm:ss.fff ";
});

// LLM Gateway + 5 Agents + PipelineOrchestrator + JSON Schema validation + Metrics
builder.Services.AddLlmGateway(builder.Configuration);
builder.Services.AddValidation();
var csvPath = builder.Configuration["Metrics:CsvPath"];
if (!string.IsNullOrWhiteSpace(csvPath))
{
    builder.Services.AddCsvMetrics(csvPath);
}
else
{
    builder.Services.AddInMemoryMetrics();
}
builder.Services.AddAgents(builder.Configuration);

// Phase 8 — IPipelineClient (in-process, since the API owns the orchestrator) +
// MutableSinkHolder so /pipeline/stream can route per-request channel sinks.
builder.Services.AddInProcessPipelineClient();

// Persistence (Postgres). Without ConnectionStrings:DefaultConnection → no-op repos.
builder.Services.AddPersistence(builder.Configuration);

// Application Insights — only register when a connection string is present (Phase 6 Azure deploy)
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddApplicationInsightsTelemetry();
}

// OpenAPI (.NET 10 native) + Scalar UI
builder.Services.AddOpenApi();

// Phase 8 — JWT bearer auth. Required on every /pipeline*, /requirement, /code, /test, /qa,
// /runs* endpoint. /health and / stay public. Auth:Mode = operator (HS256) | keycloak (OIDC).
builder.Services.AddJwtAuth(builder.Configuration);

// Keycloak mode: resolve the tenant from the OIDC token (overrides the default single-tenant context).
if (string.Equals(builder.Configuration["Auth:Mode"], "keycloak", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<AgenticSdlc.Application.Identity.ITenantContext, AgenticSdlc.Api.Auth.HttpTenantContext>();
}

// Phase 8.4b — Runtime-mutable configuration store. EF + DataProtection-encrypted when a DB is
// configured; in-memory fallback otherwise. DataProtection persists its key ring to the DataProtection
// default location (over_ridable via env for multi-instance Container Apps).
builder.Services.AddDataProtection();
builder.Services.AddAppConfigStore(builder.Configuration);

// Remote dev-IDE agent transport — SignalR hub + the broker->hub bridge (Increment 2).
builder.Services.AddSignalR();
builder.Services.AddHostedService<AgenticSdlc.Api.RemoteAgent.RemoteAgentTransport>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Apply EF migration at startup (no-op if the DB is not configured yet).
await app.Services.InitializePersistenceAsync();

// Phase 8.4b — hydrate runtime LLM/GitHub overrides from the persisted app_config table.
await app.Services.HydrateRuntimeOverridesAsync();

// Enable OpenAPI + Scalar UI in every env except Production (dev deploy runs the Staging env).
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
    version = "0.4.0-phase4",
    status = "pipeline-ready"
}))
   .WithName("Root")
   .WithSummary("Service identity")
   .WithTags("Meta");

app.MapGet("/health", (Microsoft.Extensions.Options.IOptions<AgenticSdlc.Domain.Llm.LlmOptions> llm) =>
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

app.MapAuthEndpoints();
app.MapPipelineEndpoints();
app.MapSettingsEndpoints();
app.MapTenantEndpoints();
app.MapHub<AgenticSdlc.Api.RemoteAgent.RemoteAgentHub>(AgenticSdlc.Api.RemoteAgent.RemoteAgentHub.Path);

// Settings "Test connection" — probe the configured provider with a minimal call. Uses the
// Orchestrator agent's provider+model (a matched, cheap pair; Haiku by default). Returns ok/error
// rather than throwing so the UI can show a clean result. Mock provider returns a stub → ok.
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
