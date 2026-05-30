// Composition root for the Blazor Server host. Loads the same modules as the API except RemoteAgent
// (the SignalR hub lives on the API side); auth is host-specific — Web wires Cookie + OpenID Connect
// against Keycloak realm "agentic", whereas the API uses JWT bearer. /account/login & /account/logout
// drive the challenge / sign-out round-trips; the OIDC middleware handles /signin-oidc & callback.

using System.Security.Claims;
using AgentOs.Modules.AppConfig;
using AgentOs.Modules.Identity;
using AgentOs.Modules.Identity.Auth;
using AgentOs.Modules.Integration;
using AgentOs.Modules.Llm;
using AgentOs.Modules.Pipeline;
using AgentOs.Modules.Tenants;
using AgentOs.ServiceDefaults;
using AgentOs.SharedKernel.Identity;
using AgentOs.SharedKernel.Modularity;
using AgentOs.Web.Components;
using AgentOs.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

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

// Cookie + OpenID Connect against Keycloak. The cookie carries the signed-in principal across
// HTTP requests; SaveTokens=true stores the access token in the auth cookie so circuit-scoped
// AuthSession can forward it to outbound API calls when needed.
var keycloak = builder.Configuration.GetSection("Auth:Keycloak");
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.Name = "agentic.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    })
    .AddOpenIdConnect(options =>
    {
        options.Authority = keycloak["Authority"] ?? "http://localhost:8080/realms/agentic";
        options.ClientId = keycloak["ClientId"] ?? "agentic-web";
        options.ClientSecret = keycloak["ClientSecret"] ?? "agentic-web-dev-secret";
        options.ResponseType = "code";
        options.UsePkce = true;
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.RequireHttpsMetadata = bool.TryParse(keycloak["RequireHttpsMetadata"], out var rh) && rh;
        // Realm `agentic-web` client attaches `tenant` + `realm-roles` + `preferred-username` +
        // `email` claims via inline protocol mappers — we only request the `openid` base scope.
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = "preferred_username",
            RoleClaimType = ClaimTypes.Role,
        };
        options.Events = new OpenIdConnectEvents
        {
            OnTokenValidated = ctx => { JwtAuthExtensions.FlattenRealmRoles(ctx.Principal); return Task.CompletedTask; },
        };
    });

builder.Services.AddCascadingAuthenticationState();

builder.Services.AddModulesFromAssemblies(builder.Configuration,
    typeof(AppConfigModule).Assembly,
    typeof(LlmModule).Assembly,
    typeof(IdentityModule).Assembly,
    typeof(TenantsModule).Assembly,
    typeof(PipelineModule).Assembly,
    typeof(IntegrationModule).Assembly);

builder.Services.AddSingleton<AgentOs.Web.Orchestrations.OrchestrationStore>();
builder.Services.AddSingleton<AgentOs.Web.Services.ToastService>();
builder.Services.AddSingleton<AgentOs.Web.Services.WindowManagerService>();

// Per-circuit auth session — surfaces identity + (optional) bearer to the HttpPipelineClient.
builder.Services.AddScoped<AuthSession>();
builder.Services.AddScoped<IAuthTokenProvider>(sp => sp.GetRequiredService<AuthSession>());

builder.Services.AddHttpClient();

var app = builder.Build();

await app.Services.InitializeModulesAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", utc = DateTime.UtcNow }));

// OIDC challenge / sign-out — buttons in the UI hit these endpoints. /signin-oidc and
// /signout-callback-oidc are owned by the OIDC middleware.
app.MapGet("/account/login", (string? returnUrl) =>
    Results.Challenge(
        new AuthenticationProperties { RedirectUri = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl },
        new[] { OpenIdConnectDefaults.AuthenticationScheme }));

app.MapGet("/account/logout", () =>
    Results.SignOut(
        new AuthenticationProperties { RedirectUri = "/" },
        new[] { CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme }));

app.MapStaticAssets();
app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.Run();
