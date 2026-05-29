// AgenticSdlc.Api/Program.cs
// Phase 4 — Compose: LLM Gateway + Agents + Pipeline endpoints + Scalar UI.

using AgenticSdlc.Api.Auth;
using AgenticSdlc.Api.Endpoints;
using AgenticSdlc.Application.Configuration;
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
// /runs* endpoint. /health and / stay public.
builder.Services.AddJwtAuth(builder.Configuration);

// Phase 8.4 — Runtime-mutable configuration store. In-memory until the EF entity ships.
builder.Services.AddSingleton<IAppConfigStore, InMemoryAppConfigStore>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Apply EF migration at startup (no-op if the DB is not configured yet).
await app.Services.InitializePersistenceAsync();

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

app.Run();
