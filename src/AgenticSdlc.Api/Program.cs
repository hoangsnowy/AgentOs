// AgenticSdlc.Api/Program.cs
// Phase 4 — Compose: LLM Gateway + Agents + Pipeline endpoints + Scalar UI.

using AgenticSdlc.Api.Endpoints;
using AgenticSdlc.Infrastructure.Agents;
using AgenticSdlc.Infrastructure.Llm;
using AgenticSdlc.Infrastructure.Metrics;
using AgenticSdlc.Infrastructure.Persistence;
using AgenticSdlc.Infrastructure.Validation;
using AgenticSdlc.ServiceDefaults;
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

// Persistence (Postgres). Without ConnectionStrings:DefaultConnection → no-op repos.
builder.Services.AddPersistence(builder.Configuration);

// Application Insights — only register when a connection string is present (Phase 6 Azure deploy)
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddApplicationInsightsTelemetry();
}

// OpenAPI (.NET 10 native) + Scalar UI
builder.Services.AddOpenApi();

var app = builder.Build();

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

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", utc = DateTime.UtcNow }))
   .WithName("Health")
   .WithSummary("Liveness probe")
   .WithTags("Meta");

app.MapPipelineEndpoints();

app.Run();
