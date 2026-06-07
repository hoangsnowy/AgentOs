// Composition root for the Blazor Server host. Loads the same modules as the API except RemoteAgent
// (the SignalR hub lives on the API side); auth is host-specific — Web wires Cookie + OpenID Connect
// against Keycloak realm "agentic", whereas the API uses JWT bearer. /account/login & /account/logout
// drive the challenge / sign-out round-trips; the OIDC middleware handles /signin-oidc & callback.

using System.Security.Claims;
using AgentOs.Modules.AppConfig;
using AgentOs.Web.Auth;
using AgentOs.Modules.Identity;
using AgentOs.Modules.Identity.Auth;
using AgentOs.Modules.Integration;
using AgentOs.Modules.Llm;
using AgentOs.Modules.Pipeline;
using AgentOs.Modules.Sessions;
using AgentOs.Modules.Tenants;
using AgentOs.Modules.Tools;
using AgentOs.Modules.Workspaces;
using AgentOs.ServiceDefaults;
using AgentOs.SharedKernel.Identity;
using AgentOs.SharedKernel.Modularity;
using AgentOs.SharedKernel.Plugins;
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

// Durable, shared key ring (Postgres-backed when configured) — without it the OIDC correlation/nonce
// cookies + the auth cookie + encrypted tenant secrets break on every restart/scale and don't decrypt
// across the Api ↔ Web hosts. Replaces a bare AddDataProtection() (in-memory, per-host).
builder.AddAgentOsDataProtection();

// Response compression — Brotli + Gzip for dynamic responses (the initial Razor document, /health,
// any non-static endpoint). NOTE: static assets are ALREADY compressed at build time by
// MapStaticAssets, and Blazor Server's per-interaction traffic rides a WebSocket (which this
// middleware does not touch), so this trims first-load transfer — it is not a fix for interaction
// latency (that is build config + SignalR round-trips). Fastest level keeps CPU cost low.
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    o.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
});
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProviderOptions>(
    o => o.Level = System.IO.Compression.CompressionLevel.Fastest);
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProviderOptions>(
    o => o.Level = System.IO.Compression.CompressionLevel.Fastest);

// Dev single-command run: auto-authenticate as a fixed developer principal so `dotnet run --project
// src/AgentOs.Web` shows the desktop with no Keycloak / Postgres. Defaults ON in Development (so a fresh
// clone just works — appsettings.Development.json is gitignored and can't be relied on); the AppHost
// injects Auth__DevAutoLogin=false so the full stack uses real OIDC, and it is hard-off outside Development.
var devAutoLogin = builder.Configuration.GetValue("Auth:DevAutoLogin", builder.Environment.IsDevelopment());
if (devAutoLogin && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "Auth:DevAutoLogin must never be enabled outside Development — it authenticates every request as a fixed user.");
}

// Surface dev-mode to the UI so dev-only affordances (the "View as" role-preview menu) render only
// when auto-login is active, never under real Keycloak auth.
builder.Services.AddSingleton(new AgentOs.Web.Services.DevModeState(devAutoLogin));

if (devAutoLogin)
{
    builder.Services
        .AddAuthentication(DevAutoAuthHandler.SchemeName)
        .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, DevAutoAuthHandler>(
            DevAutoAuthHandler.SchemeName, _ => { });
}
else
{

// Cookie + OpenID Connect against Keycloak. The cookie carries the signed-in principal across
// HTTP requests; SaveTokens=true stores the access token in the auth cookie so circuit-scoped
// AuthSession can forward it to outbound API calls when needed.
var keycloak = builder.Configuration.GetSection("Auth:Keycloak");
var keycloakClientSecret = keycloak["ClientSecret"];
if (string.IsNullOrWhiteSpace(keycloakClientSecret) && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "Auth:Keycloak:ClientSecret is required outside the Development environment. " +
        "Set it via Aspire parameters, user-secrets, or an environment variable.");
}
// Default true: code that forgets to override picks the secure setting. Dev overrides via
// appsettings.Development.json or the AppHost env injection.
var requireHttps = !bool.TryParse(keycloak["RequireHttpsMetadata"], out var rh) || rh;
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
        // Always send the cookie over HTTPS in non-Development; in dev we may run plain http://
        // (Aspire pins port 5180), so flex with the request scheme.
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    })
    .AddOpenIdConnect(options =>
    {
        options.Authority = keycloak["Authority"] ?? "http://localhost:8080/realms/agentic";
        options.ClientId = keycloak["ClientId"] ?? "agentic-web";
        options.ClientSecret = keycloakClientSecret ?? string.Empty;
        options.ResponseType = "code";
        options.UsePkce = true;
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.RequireHttpsMetadata = requireHttps;
        // Correlation + nonce cookies default to SameSite=None, which the browser only stores when
        // also Secure. Behind Aspire's dcp TLS-terminating proxy the app can see the request as http,
        // so the cookie is written WITHOUT Secure → Chrome drops it → "Correlation failed" at
        // /signin-oidc. The code flow's callback is a top-level GET, so SameSite=Lax is sufficient and
        // is sent without requiring Secure; SameAsRequest keeps it Secure when the scheme really is https.
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.NonceCookie.SameSite = SameSiteMode.Lax;
        options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
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

} // end else (real Keycloak OIDC)

builder.Services.AddCascadingAuthenticationState();

builder.Services.AddModulesFromAssemblies(builder.Configuration,
    typeof(AppConfigModule).Assembly,
    typeof(LlmModule).Assembly,
    typeof(IdentityModule).Assembly,
    typeof(TenantsModule).Assembly,
    typeof(PipelineModule).Assembly,
    typeof(IntegrationModule).Assembly,
    typeof(WorkspacesModule).Assembly,
    typeof(SessionsModule).Assembly,
    typeof(ToolsModule).Assembly);

// Runtime plugins: discover IAgentOsPlugin assemblies dropped in the plugins folder (Plugins:Path,
// default "plugins" under the content root). A missing folder is a no-op.
var pluginsPath = builder.Configuration["Plugins:Path"] ?? "plugins";
builder.Services.AddPlugins(builder.Configuration,
    System.IO.Path.IsPathRooted(pluginsPath)
        ? pluginsPath
        : System.IO.Path.Join(builder.Environment.ContentRootPath, pluginsPath));

builder.Services.AddSingleton<AgentOs.Web.Orchestrations.OrchestrationStore>();
// Per-circuit UI state: each user's desktop has its own open windows. Singleton would bleed windows
// (and their Z-order) across every connected circuit/user on the server.
builder.Services.AddScoped<AgentOs.Web.Services.ToastService>();
builder.Services.AddScoped<AgentOs.Web.Services.WindowManagerService>();
builder.Services.AddScoped<AgentOs.Web.Orchestrations.GraphRunnerService>();
builder.Services.AddScoped<AgentOs.Web.Services.WorkspacePrTargetService>();

// Per-circuit auth session — surfaces identity + (optional) bearer to the HttpPipelineClient.
builder.Services.AddScoped<AuthSession>();
builder.Services.AddScoped<IAuthTokenProvider>(sp => sp.GetRequiredService<AuthSession>());

builder.Services.AddHttpClient();

var app = builder.Build();

await app.Services.InitializeModulesAsync();

// Surface plugin-contributed desktop windows: collect every PluginAppDescriptor a plugin registered and
// add it to the shell catalog (the WindowHost renders these via DynamicComponent).
AppCatalog.RegisterPluginApps(app.Services.GetServices<PluginAppDescriptor>().Select(d =>
    new DesktopApp(d.Key, d.Title, d.Icon, d.Caption, "Plugins", d.Width, d.Height, Pinned: true,
        AdminOnly: d.AdminOnly, ComponentType: d.ComponentType)));

// FIRST middleware: honour X-Forwarded-Proto/For from the Container Apps ingress. Critical for the Web
// — the OIDC redirect_uri + secure-cookie decisions depend on the request being seen as https.
app.UseAgentOsForwardedHeaders();

// Early in the pipeline so it wraps every downstream response. Skips assets MapStaticAssets already
// served pre-compressed (their Content-Encoding is set), so there is no double-compression.
app.UseResponseCompression();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", utc = DateTime.UtcNow }));

// E4 — liveness probe. Distinct from readiness: returns 200 as long as the process can serve a
// request, so a hung instance fails liveness and Container Apps recycles it. Mapped unconditionally
// (the old shared MapDefaultEndpoints gated this behind IsDevelopment, so prod had no liveness target).
app.MapGet("/alive", () => Results.Ok(new { status = "Alive", utc = DateTime.UtcNow }));

// Serve the self-contained AgentOs.RemoteAgent exe so the VS Code extension (and the Runners tab) can
// fetch the runner with no .NET SDK and no source checkout. scripts/build-runner.ps1 publishes one
// single-file binary per RID into runner-dist/<rid>/; the extension sends ?rid= for its OS. win-x64 is
// the default and also falls back to the legacy flat path. Anonymous — the runner is the public
// open-source binary and carries no secrets (pairing happens via a separate token).
//
// Resolve the published binary path for a RID, or null if that platform isn't built / RID is unknown.
static string? ResolveRunnerPath(string contentRoot, string rid)
{
    string[] known = ["win-x64", "linux-x64", "osx-x64", "osx-arm64"];
    if (!known.Contains(rid))
    {
        return null;
    }
    var file = rid.StartsWith("win", StringComparison.Ordinal) ? "AgentOs.RemoteAgent.exe" : "AgentOs.RemoteAgent";
    var path = System.IO.Path.Join(contentRoot, "runner-dist", rid, file);
    if (System.IO.File.Exists(path))
    {
        return path;
    }
    // Legacy flat layout (pre multi-RID builds) — Windows only.
    if (rid == "win-x64")
    {
        var legacy = System.IO.Path.Join(contentRoot, "runner-dist", "AgentOs.RemoteAgent.exe");
        if (System.IO.File.Exists(legacy))
        {
            return legacy;
        }
    }
    return null;
}

app.MapGet("/runner/download", (IHostEnvironment env, string? rid) =>
{
    var resolved = ResolveRunnerPath(env.ContentRootPath, string.IsNullOrWhiteSpace(rid) ? "win-x64" : rid);
    if (resolved is null)
    {
        return Results.NotFound(
            $"No runner binary for '{rid ?? "win-x64"}'. Run scripts/build-runner.ps1 on the server to publish it.");
    }
    var downloadName = resolved.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? "agentos-runner.exe" : "agentos-runner";
    return Results.File(resolved, "application/octet-stream", downloadName);
});

// Version probe for the extension's auto-update: a cheap build token (size + mtime) of the published
// binary for a RID. The extension stores it and re-downloads when it changes. No assembly-version
// plumbing needed; the token only has to differ when the bytes do.
app.MapGet("/runner/version", (IHostEnvironment env, string? rid) =>
{
    var resolved = ResolveRunnerPath(env.ContentRootPath, string.IsNullOrWhiteSpace(rid) ? "win-x64" : rid);
    if (resolved is null)
    {
        return Results.NotFound($"No runner binary for '{rid ?? "win-x64"}'.");
    }
    var info = new System.IO.FileInfo(resolved);
    var token = string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"{info.Length:x}-{info.LastWriteTimeUtc.Ticks:x}");
    return Results.Text(token);
});

// Cost CSV export for the admin Cost app. Mapped here on the Web (not via the API's MapModuleEndpoints)
// so the Cost circuit can trigger a same-origin, cookie-authed browser download — like /runner/download.
// Tenant comes from the OIDC 'tenant' claim; admin-gated; `days` (0/absent = all time) sets the cutoff.
app.MapGet("/cost/export", async (HttpContext http, AgentOs.Modules.Pipeline.Persistence.IPipelineRunRepository runs, int? days) =>
{
    if (!http.User.IsInRole("admin"))
    {
        return Results.Forbid();
    }

    var tenant = http.User.FindFirst("tenant")?.Value is { Length: > 0 } t ? t : "default";
    DateTimeOffset? since = days is > 0 ? DateTimeOffset.UtcNow.AddDays(-days.Value) : null;
    var summary = await runs.GetCostSummaryForTenantAsync(tenant, since);
    var bytes = System.Text.Encoding.UTF8.GetBytes(AgentOs.Modules.Pipeline.Cost.CostCsv.ToCsv(summary));
    return Results.File(bytes, "text/csv", "agentos-cost.csv");
}).RequireAuthorization();

// VS Code browser-pairing: the approve page (/pair/vscode, OIDC-cookie authed) + the one-time code
// exchange (/runner/pair/exchange). Mapped here on the Web — the extension and browser both target the
// Web origin, and the approve step uses the browser session, not a Bearer token.
AgentOs.Modules.Sessions.Endpoints.PairingEndpoints.MapPairingEndpoints(app);

// OIDC challenge / sign-out — buttons in the UI hit these endpoints. /signin-oidc and
// /signout-callback-oidc are owned by the OIDC middleware. In dev-auto-login mode there are no
// Cookie/OIDC schemes, so these become simple redirects (the dev user is always signed in).
if (devAutoLogin)
{
    app.MapGet("/account/login", (string? returnUrl) =>
        Results.Redirect(string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl));
    app.MapGet("/account/logout", () => Results.Redirect("/"));

    // Dev-only "View as": narrow the auto-login principal's roles to preview the member (or admin)
    // desktop without standing up a second Keycloak user. Writes the cookie DevAutoAuthHandler reads,
    // then reloads so the new principal takes effect. Available only because devAutoLogin is on.
    app.MapGet("/dev/view-as", (HttpContext ctx, string? role) =>
    {
        if (role is "admin" or "member")
        {
            ctx.Response.Cookies.Append(DevAutoAuthHandler.ViewAsCookie, role,
                new Microsoft.AspNetCore.Http.CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
                    Path = "/",
                });
        }
        else
        {
            ctx.Response.Cookies.Delete(DevAutoAuthHandler.ViewAsCookie);
        }
        return Results.Redirect("/");
    });
}
else
{
    app.MapGet("/account/login", (string? returnUrl) =>
        Results.Challenge(
            new AuthenticationProperties { RedirectUri = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl },
            new[] { OpenIdConnectDefaults.AuthenticationScheme }));

    app.MapGet("/account/logout", () =>
        Results.SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            new[] { CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme }));
}

app.MapStaticAssets();
app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.Run();
