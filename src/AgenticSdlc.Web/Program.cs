// Composition root for the Blazor Server host. Same modules as the API except RemoteAgent (the SignalR
// hub lives on the API side); Web maps a Razor component pipeline + a couple of host services.

using AgenticSdlc.Modules.AppConfig;
using AgenticSdlc.Modules.Identity;
using AgenticSdlc.Modules.Integration;
using AgenticSdlc.Modules.Llm;
using AgenticSdlc.Modules.Pipeline;
using AgenticSdlc.Modules.Tenants;
using AgenticSdlc.ServiceDefaults;
using AgenticSdlc.SharedKernel.Identity;
using AgenticSdlc.SharedKernel.Modularity;
using AgenticSdlc.Web.Components;
using AgenticSdlc.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Logging.AddSimpleConsole(options =>
{
    options.IncludeScopes = true;
    options.SingleLine = false;
    options.TimestampFormat = "HH:mm:ss.fff ";
});

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDataProtection();

builder.Services.AddModulesFromAssemblies(builder.Configuration,
    typeof(AppConfigModule).Assembly,
    typeof(LlmModule).Assembly,
    typeof(IdentityModule).Assembly,
    typeof(TenantsModule).Assembly,
    typeof(PipelineModule).Assembly,
    typeof(IntegrationModule).Assembly);

builder.Services.AddSingleton<AgenticSdlc.Web.Orchestrations.OrchestrationStore>();
builder.Services.AddSingleton<AgenticSdlc.Web.Services.ToastService>();
builder.Services.AddSingleton<AgenticSdlc.Web.Services.WindowManagerService>();

// Per-circuit auth session — replaces the default null IAuthTokenProvider for HTTP pipeline client.
builder.Services.AddScoped<AuthSession>();
builder.Services.AddScoped<IAuthTokenProvider>(sp => sp.GetRequiredService<AuthSession>());

builder.Services.AddHttpClient();

var app = builder.Build();

await app.Services.InitializeModulesAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseAntiforgery();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", utc = DateTime.UtcNow }));

app.MapStaticAssets();
app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.Run();
