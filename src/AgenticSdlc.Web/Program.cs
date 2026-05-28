// AgenticSdlc.Web/Program.cs
// Blazor Server host for the Agent Studio. Composes the Infrastructure LLM Gateway + 5 agents +
// PipelineOrchestrator with a circuit-scoped progress sink so the UI can render the run live.

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

// Build verifier — runs `dotnet build` on the generated code in a temp directory.
builder.Services.AddBuildVerifier();

// --- Presentation-layer scopes ---

// Per-circuit progress sink — the orchestrator reports, the component listens then re-renders.
builder.Services.AddScoped<CircuitPipelineProgress>();
builder.Services.AddScoped<IPipelineProgressSink>(sp => sp.GetRequiredService<CircuitPipelineProgress>());

// Orchestration store (drag-and-drop editor graphs) — singleton, seeds + saves JSON.
builder.Services.AddSingleton<AgenticSdlc.Web.Orchestrations.OrchestrationStore>();

// Toast notification bus — any page can push, ToastHost listens.
builder.Services.AddSingleton<AgenticSdlc.Web.Services.ToastService>();

// Window manager — tracks open desktop-app windows.
builder.Services.AddSingleton<AgenticSdlc.Web.Services.WindowManagerService>();

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
