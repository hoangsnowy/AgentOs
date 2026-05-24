// AgenticSdlc.Web/Program.cs
// Phase 7 — Host trình diễn realtime (Blazor Server / InteractiveServer).
// Tái sử dụng nguyên LLM Gateway + 5 agent + PipelineOrchestrator của Infrastructure,
// chỉ override 2 chỗ: nguồn LLM (cho phép chế độ Demo offline) và cổng phát tiến trình.

using AgenticSdlc.Application.Pipeline;
using AgenticSdlc.Infrastructure.Agents;
using AgenticSdlc.Infrastructure.Llm;
using AgenticSdlc.Infrastructure.Metrics;
using AgenticSdlc.Infrastructure.Persistence;
using AgenticSdlc.Infrastructure.Validation;
using AgenticSdlc.Web.Components;
using AgenticSdlc.Web.Services;
using AgenticSdlc.Web.Services.Demo;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddSimpleConsole(options =>
{
    options.IncludeScopes = true;
    options.SingleLine = false;
    options.TimestampFormat = "HH:mm:ss.fff ";
});

// Blazor Server (Interactive Server render mode) — circuit chạy trên SignalR, đẩy UI realtime.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Lõi pipeline (giống Api): Gateway + validation + metrics + 5 agent + orchestrator.
builder.Services.AddLlmGateway(builder.Configuration);
builder.Services.AddValidation();
builder.Services.AddInMemoryMetrics();
builder.Services.AddAgents(builder.Configuration);

// Persistence (Postgres). Không có ConnectionStrings:DefaultConnection → no-op repos (in-memory).
builder.Services.AddPersistence(builder.Configuration);

// --- Override cho lớp trình diễn ---

// 1) Nguồn LLM nhận biết chế độ Demo: UseDemo ⇒ trả JSON canned (chạy offline, có Quality Loop);
//    ngược lại ⇒ uỷ quyền cho LlmClientFactory thật (Claude / Azure OpenAI theo appsettings).
builder.Services.AddSingleton<LlmClientFactory>();
builder.Services.AddScoped<DemoRunContext>();
builder.Services.AddScoped<DemoLlmClient>();
builder.Services.AddScoped<ILlmClientFactory, DemoAwareLlmClientFactory>();

// 2) Cổng phát tiến trình theo từng circuit — orchestrator báo, component lắng nghe rồi re-render.
builder.Services.AddScoped<CircuitPipelineProgress>();
builder.Services.AddScoped<IPipelineProgressSink>(sp => sp.GetRequiredService<CircuitPipelineProgress>());

// 3) Kho orchestration (đồ thị editor kéo-thả) — singleton, seed + lưu JSON.
builder.Services.AddSingleton<AgenticSdlc.Web.Orchestrations.OrchestrationStore>();

var app = builder.Build();

// Apply EF migration lúc startup (no-op nếu chưa cấu hình DB).
await app.Services.InitializePersistenceAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.Run();
