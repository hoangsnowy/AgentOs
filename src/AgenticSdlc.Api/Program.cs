// AgenticSdlc.Api/Program.cs
// Phase 4 — Compose: LLM Gateway + Agents + Pipeline endpoints + Scalar UI.

using AgenticSdlc.Api.Endpoints;
using AgenticSdlc.Infrastructure.Agents;
using AgenticSdlc.Infrastructure.Llm;
using AgenticSdlc.Infrastructure.Validation;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Logging
builder.Logging.AddSimpleConsole(options =>
{
    options.IncludeScopes = true;
    options.SingleLine = false;
    options.TimestampFormat = "HH:mm:ss.fff ";
});

// LLM Gateway + 5 Agents + PipelineOrchestrator + JSON Schema validation
builder.Services.AddLlmGateway(builder.Configuration);
builder.Services.AddValidation();
builder.Services.AddAgents(builder.Configuration);

// Application Insights — chỉ register khi có connection string (Phase 6 Azure deploy)
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddApplicationInsightsTelemetry();
}

// OpenAPI (.NET 10 native) + Scalar UI
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
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
