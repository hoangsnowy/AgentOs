// AgenticSdlc.Web/Program.cs
// Blazor Server host for the Agent Studio. Composes the Infrastructure LLM Gateway + 5 agents +
// PipelineOrchestrator with a circuit-scoped progress sink so the UI can render the run live.

using AgenticSdlc.Application.Auth;
using AgenticSdlc.Application.Pipeline;
using AgenticSdlc.Infrastructure.Agents;
using AgenticSdlc.Infrastructure.Integration;
using AgenticSdlc.Infrastructure.Llm;
using AgenticSdlc.Infrastructure.Metrics;
using AgenticSdlc.Infrastructure.Persistence;
using AgenticSdlc.Infrastructure.Pipeline;
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

// Phase 8 — IPipelineClient transport. Defaults to HTTP (Web -> API) when Api:BaseUrl is set;
// falls back to in-process for single-host dev. When in-process, Web also owns the
// MutableSinkHolder so the orchestrator's progress events route into our channel.
var apiBaseUrl = builder.Configuration["Api:BaseUrl"];
if (string.IsNullOrWhiteSpace(apiBaseUrl))
{
    builder.Services.AddInProcessPipelineClient();
}
else
{
    builder.Services.AddHttpPipelineClient(builder.Configuration);
}

// Persistence (Postgres). Without ConnectionStrings:DefaultConnection → no-op repos (in-memory).
builder.Services.AddPersistence(builder.Configuration);

// GitHub integration — IGitHubPrService for opening a PR with the generated code.
builder.Services.AddGitHubIntegration();

// Build verifier — runs `dotnet build` on the generated code in a temp directory.
builder.Services.AddBuildVerifier();

// --- Presentation-layer scopes ---

// Phase 8 — CircuitPipelineProgress removed. PipelineStudio now consumes IPipelineClient.StreamAsync
// directly; the orchestrator's IPipelineProgressSink lives in MutableSinkHolder
// (registered by AddInProcessPipelineClient) when running in single-host dev mode.

// Orchestration store (drag-and-drop editor graphs) — singleton, seeds + saves JSON.
builder.Services.AddSingleton<AgenticSdlc.Web.Orchestrations.OrchestrationStore>();

// Toast notification bus — any page can push, ToastHost listens.
builder.Services.AddSingleton<AgenticSdlc.Web.Services.ToastService>();

// Window manager — tracks open desktop-app windows.
builder.Services.AddSingleton<AgenticSdlc.Web.Services.WindowManagerService>();

// Phase 8.3 — Per-circuit auth session. Holds the JWT after login and exposes it via
// IAuthTokenProvider so HttpPipelineClient attaches the bearer header per request.
builder.Services.AddScoped<AgenticSdlc.Web.Services.AuthSession>();
builder.Services.AddScoped<IAuthTokenProvider>(sp => sp.GetRequiredService<AgenticSdlc.Web.Services.AuthSession>());

// Generic HttpClient factory — LoginOverlay reuses this to POST /auth/token.
builder.Services.AddHttpClient();

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
