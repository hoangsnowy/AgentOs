// AgenticSdlc.Api/Program.cs
// Minimal API host (.NET 10). DI và endpoint sẽ được hoàn thiện ở các Phase tiếp theo.

using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Logging
builder.Logging.AddSimpleConsole(options =>
{
    options.IncludeScopes = true;
    options.SingleLine = false;
    options.TimestampFormat = "HH:mm:ss.fff ";
});

// (Phase 2) ServiceCollection extensions sẽ đăng ký:
//   - LLM Gateway (ILlmClient, ClaudeClient, AzureOpenAiClient, ILlmClientFactory)
//   - 5 agent (IRequirementAgent / ICodingAgent / ...)
//   - PipelineOrchestrator
// builder.Services.AddAgenticSdlcInfrastructure(builder.Configuration);

// OpenAPI (.NET 10 native) + Scalar UI
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // /openapi/v1.json — spec
    app.MapOpenApi();

    // /scalar/v1 — UI hiện đại của Scalar
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
    version = "0.1.0-phase1",
    status = "scaffold-ready"
}))
   .WithName("Root")
   .WithSummary("Service identity")
   .WithTags("Meta");

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", utc = DateTime.UtcNow }))
   .WithName("Health")
   .WithSummary("Liveness probe")
   .WithTags("Meta");

// (Phase 4) Pipeline endpoints sẽ được thêm tại đây:
//   app.MapAgenticSdlcEndpoints();

app.Run();
