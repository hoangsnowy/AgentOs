// Aspire AppHost — single F5 brings up Postgres + Keycloak + MailHog + API + Web for local dev.
// azd promotes Postgres to Azure Database for PostgreSQL flexible server in the cloud. The DB
// resource is named "DefaultConnection" so WithReference injects ConnectionStrings__DefaultConnection.
// Keycloak realm "agentic" is auto-imported from ./infra/keycloak; its HTTP URL is forwarded as
// Auth__Keycloak__Authority so the API (JWT bearer) and the Web (OIDC code flow) both wire against
// it without any hardcoded URL. Web pins the HTTP endpoint to 5180 to match the realm's
// `agentic-web` client redirectUris. MailHog catches all dev verification emails on UI port 8025;
// Keycloak sends to it via the realm-level smtpServer config (host=mailhog port=1025).
//
// Secrets (Keycloak admin password + agentic-web client secret) are Aspire Parameters; their dev
// defaults live in appsettings.json under "Parameters". Override per-environment via
// `dotnet user-secrets`, `azd env set`, or environment variables — never edit the dev defaults.
using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var kcAdminUsername = builder.AddParameter("KeycloakAdminUsername");
var kcAdminPassword = builder.AddParameter("KeycloakAdminPassword", secret: true);
var kcWebClientSecret = builder.AddParameter("KeycloakWebClientSecret", secret: true);

var db = builder.AddAzurePostgresFlexibleServer("postgres")
    .RunAsContainer(c => c.WithDataVolume())
    .AddDatabase("DefaultConnection", databaseName: "agentos");

// MailHog — dev SMTP catcher. Realm smtpServer points here; UI at http://localhost:8025 shows
// every verification email Keycloak emits during signup. Container name "mailhog" doubles as the
// hostname Keycloak resolves over the Aspire container network.
var mailhog = builder.AddContainer("mailhog", "mailhog/mailhog")
    .WithHttpEndpoint(port: 8025, targetPort: 8025, name: "ui")
    .WithEndpoint(port: 1025, targetPort: 1025, name: "smtp", scheme: "tcp");

var keycloak = builder.AddKeycloak("keycloak", port: 8080)
    .WithDataVolume()
    .WithRealmImport("../../infra/keycloak")
    // Custom AgentOs login/account theme — Breeze look matching the Web Studio. KC scans
    // /opt/keycloak/themes/<name>/ for themes; bind-mount our source dir so edits to the CSS
    // hot-reload without rebuilding the container (KC auto-detects in dev mode).
    .WithBindMount("../../infra/keycloak/themes/agentos", "/opt/keycloak/themes/agentos")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", kcAdminUsername)
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", kcAdminPassword)
    // Dev only: disable theme caching so CSS edits show up on reload (default is 24h cache).
    .WithEnvironment("KC_SPI_THEME_STATIC_MAX_AGE", "-1")
    .WithEnvironment("KC_SPI_THEME_CACHE_THEMES", "false")
    .WithEnvironment("KC_SPI_THEME_CACHE_TEMPLATES", "false")
    // Dev only: localhost shares one cookie jar across EVERY dev app on the box, so the Cookie header
    // sent to Keycloak can blow past Quarkus's default max header size → HTTP 431 on /auth. Raise the
    // ceiling (a real Keycloak on its own domain never sees this pollution).
    .WithEnvironment("QUARKUS_HTTP_LIMITS_MAX_HEADER_SIZE", "64K")
    .WaitFor(mailhog);

builder.AddProject<Projects.AgentOs_Api>("api")
    .WithReference(db).WaitFor(db)
    .WithReference(keycloak).WaitFor(keycloak)
    .WithEnvironment("Auth__Keycloak__Authority",
        ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/agentic"))
    .WithEnvironment("Auth__Keycloak__Audience", "agentic-api")
    // Admin REST — /tenants and member-invite endpoints provision realm users via this.
    .WithEnvironment("Auth__Keycloak__Admin__BaseUrl", keycloak.GetEndpoint("http"))
    .WithEnvironment("Auth__Keycloak__Admin__Realm", "agentic")
    .WithEnvironment("Auth__Keycloak__Admin__Username", kcAdminUsername)
    .WithEnvironment("Auth__Keycloak__Admin__Password", kcAdminPassword)
    .WithEnvironment("Auth__Keycloak__Admin__ClientId", "admin-cli")
    // App-sent mail (MailKit) → MailHog in dev. Prod injects a real provider via secrets.
    .WithEnvironment("Email__SmtpHost", "localhost")
    .WithEnvironment("Email__SmtpPort", "1025")
    .WithEnvironment("Email__From", "noreply@agentic.local")
    .WithEnvironment("Email__FromName", "AgentOS")
    .WaitFor(mailhog);

// The Web must be reachable at EXACTLY https://localhost:5180 — that string is hard-wired into the
// Keycloak realm's redirectUris, so any other scheme/port/origin breaks OIDC login. Make that binding
// the single source of truth, with no possibility of a port clash:
//   • launchProfileName: null     — ignore launchSettings (its "http" profile pins http://localhost:5180,
//                                    which otherwise wins and makes Kestrel serve plain HTTP → the
//                                    browser gets ERR_SSL_PROTOCOL_ERROR and OIDC builds a http://
//                                    redirect_uri the realm rejects ('Invalid parameter: redirect_uri').
//   • isProxied: false + port==targetPort  — Kestrel binds https://localhost:5180 DIRECTLY (no Aspire
//                                    reverse-proxy in front), so nothing else contends for 5180 and the
//                                    request origin the app sees IS https://localhost:5180 → the OIDC
//                                    redirect_uri matches the realm with no forwarded-header juggling.
// (Standalone `dotnet run --project src/AgentOs.Web` is unaffected — it still uses its own launchSettings.)
builder.AddProject<Projects.AgentOs_Web>("web", launchProfileName: null)
    .WithHttpsEndpoint(port: 5180, targetPort: 5180, name: "https", isProxied: false)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    // Full stack uses real Keycloak OIDC — turn OFF the standalone dev-run auto-login.
    .WithEnvironment("Auth__DevAutoLogin", "false")
    .WithReference(db).WaitFor(db)
    .WithReference(keycloak).WaitFor(keycloak)
    .WithEnvironment("Auth__Keycloak__Authority",
        ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/agentic"))
    .WithEnvironment("Auth__Keycloak__Audience", "agentic-api")
    .WithEnvironment("Auth__Keycloak__ClientId", "agentic-web")
    .WithEnvironment("Auth__Keycloak__ClientSecret", kcWebClientSecret)
    // Web provisions Keycloak users for the public sign-up form, so it also needs admin creds.
    .WithEnvironment("Auth__Keycloak__Admin__BaseUrl", keycloak.GetEndpoint("http"))
    .WithEnvironment("Auth__Keycloak__Admin__Realm", "agentic")
    .WithEnvironment("Auth__Keycloak__Admin__Username", kcAdminUsername)
    .WithEnvironment("Auth__Keycloak__Admin__Password", kcAdminPassword)
    .WithEnvironment("Auth__Keycloak__Admin__ClientId", "admin-cli")
    // App-sent mail (MailKit) → MailHog in dev. Prod injects a real provider via secrets.
    .WithEnvironment("Email__SmtpHost", "localhost")
    .WithEnvironment("Email__SmtpPort", "1025")
    .WithEnvironment("Email__From", "noreply@agentic.local")
    .WithEnvironment("Email__FromName", "AgentOS")
    .WaitFor(mailhog);

builder.Build().Run();
