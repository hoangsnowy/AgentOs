// AgenticSdlc.Web/Program.cs
// Phase 7 — Realtime demo host (Blazor Server / InteractiveServer).
// Reuses Infrastructure's LLM Gateway + 5 agents + PipelineOrchestrator as-is,
// overriding only 2 things: the LLM source (to allow offline Demo mode) and the progress sink.

using AgenticSdlc.Application.Pipeline;
using AgenticSdlc.Infrastructure.Agents;
using AgenticSdlc.Infrastructure.Integration;
using AgenticSdlc.Infrastructure.Llm;
using AgenticSdlc.Infrastructure.Metrics;
using AgenticSdlc.Infrastructure.Persistence;
using AgenticSdlc.Infrastructure.Validation;
using AgenticSdlc.ServiceDefaults;
using AgenticSdlc.Web.Components;
using AgenticSdlc.Web.Services;
using AgenticSdlc.Web.Services.Demo;

var builder = WebApplication.CreateBuilder(args);

// Aspire service defaults: OpenTelemetry, health checks, service discovery, HTTP resilience.
builder.AddServiceDefaults();

builder.Logging.AddSimpleConsole(options =>
{
    options.IncludeScopes = true;
    options.SingleLine = false;
    options.TimestampFormat = "HH:mm:ss.fff ";
});

// Blazor Server (Interactive Server render mode) — the circuit runs over SignalR, pushing UI in realtime.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Pipeline core (same as the Api): Gateway + validation + metrics + 5 agents + orchestrator.
builder.Services.AddLlmGateway(builder.Configuration);
builder.Services.AddValidation();
builder.Services.AddInMemoryMetrics();
builder.Services.AddAgents(builder.Configuration);

// Persistence (Postgres). Without ConnectionStrings:DefaultConnection → no-op repos (in-memory).
builder.Services.AddPersistence(builder.Configuration);

// GitHub integration — IGitHubPrService for opening a PR with the generated code.
builder.Services.AddGitHubIntegration();

// --- Overrides for the presentation layer ---

// 1) Demo-aware LLM source: UseDemo ⇒ returns canned JSON (runs offline, with the Quality Loop);
//    otherwise ⇒ delegates to the real LlmClientFactory (Claude / Azure OpenAI per appsettings).
builder.Services.AddSingleton<LlmClientFactory>();
builder.Services.AddScoped<DemoRunContext>();
builder.Services.AddScoped<DemoLlmClient>();
builder.Services.AddScoped<ILlmClientFactory, DemoAwareLlmClientFactory>();

// 2) Per-circuit progress sink — the orchestrator reports, the component listens then re-renders.
builder.Services.AddScoped<CircuitPipelineProgress>();
builder.Services.AddScoped<IPipelineProgressSink>(sp => sp.GetRequiredService<CircuitPipelineProgress>());

// 3) Orchestration store (drag-and-drop editor graphs) — singleton, seeds + saves JSON.
builder.Services.AddSingleton<AgenticSdlc.Web.Orchestrations.OrchestrationStore>();

var app = builder.Build();

// Apply the EF migration at startup (no-op if the DB is not configured).
await app.Services.InitializePersistenceAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseAntiforgery();

// Liveness/readiness probe for Container Apps.
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", utc = DateTime.UtcNow }));

// .NET 9/10 static asset delivery — serves wwwroot + _framework/blazor.web.js in
// published/container mode (UseStaticFiles alone 404s the framework script when published).
app.MapStaticAssets();
app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.Run();
