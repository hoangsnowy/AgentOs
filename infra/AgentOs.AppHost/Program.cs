// Aspire AppHost — single F5 brings up Postgres + Keycloak + MailHog + API + Web for local dev.
// azd promotes Postgres to Azure Database for PostgreSQL flexible server in the cloud. The DB
// resource is named "DefaultConnection" so WithReference injects ConnectionStrings__DefaultConnection.
// Keycloak realm "agentic" is auto-imported from ./infra/keycloak; its HTTP URL is forwarded as
// Auth__Keycloak__Authority so the API (JWT bearer) and the Web (OIDC code flow) both wire against
// it without any hardcoded URL. Web pins the HTTP endpoint to 5180 to match the realm's
// `agentic-web` client redirectUris. MailHog catches all dev verification emails on UI port 8025;
// Keycloak sends to it via the realm-level smtpServer config (host=mailhog port=1025).
//
// --- Cloud bootstrap (two-step) ---
// Step 1: azd provision  → provisions Azure Postgres + Container Apps infrastructure.
//         KC starts ephemeral (H2) on first deploy — app works but realm resets on restarts.
// Step 2: Create a 'keycloak' database on the provisioned Postgres server, then:
//         azd env set KEYCLOAKDBURL      "jdbc:postgresql://<host>:5432/keycloak?sslmode=require"
//         azd env set KEYCLOAKDBUSERNAME "<admin-login>"
//         azd env set KEYCLOAKDBPASSWORD "<password>"
//         azd env set KEYCLOAKHOSTNAME   "https://keycloak.<env>.<region>.azurecontainerapps.io"
//         azd env set KEYCLOAKADMINPASSWORD    "<strong-password>"
//         azd env set KEYCLOAKWEBCLIENTSECRET  "<strong-secret>"
//         azd env set SMTPHOST  "<smtp-relay-host>"
//         azd env set SMTPPORT  "587"
//         azd up  → KC now uses Postgres (durable) + stable hostname
//
// Secrets (Keycloak admin password + agentic-web client secret) are Aspire Parameters; their dev
// defaults live in appsettings.json under "Parameters". Override per-environment via
// `dotnet user-secrets`, `azd env set`, or environment variables — never edit the dev defaults.
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

// Run mode = local `dotnet run` (single-F5 dev stack). Publish mode = `azd`/manifest generation for the
// cloud. Dev-only affordances (theme bind-mount, MailHog, Web Development pin) are gated to run mode.
var isPublish = builder.ExecutionContext.IsPublishMode;

// --- Auth parameters ---
var kcAdminUsername   = builder.AddParameter("KeycloakAdminUsername");
var kcAdminPassword   = builder.AddParameter("KeycloakAdminPassword", secret: true);
var kcWebClientSecret = builder.AddParameter("KeycloakWebClientSecret", secret: true);

// --- Cloud KC persistence + hostname (empty in dev; set via azd env set — see bootstrap comment above) ---
var kcDbUrl      = builder.AddParameter("KeycloakDbUrl");
var kcDbUsername = builder.AddParameter("KeycloakDbUsername");
var kcDbPassword = builder.AddParameter("KeycloakDbPassword", secret: true);
var kcHostname   = builder.AddParameter("KeycloakHostname");

// --- Email (dev → MailHog localhost:1025; cloud → real SMTP via azd env set SMTPHOST / SMTPPORT) ---
var smtpHost = builder.AddParameter("SmtpHost");
var smtpPort = builder.AddParameter("SmtpPort");

// --- App database (local: Docker container; cloud: Azure Postgres Flexible Server) ---
var db = builder.AddAzurePostgresFlexibleServer("postgres")
    .RunAsContainer(c => c.WithDataVolume())
    .AddDatabase("DefaultConnection", databaseName: "agentos");

// --- MailHog: dev SMTP catcher only — never ship to cloud ---
IResourceBuilder<ContainerResource>? mailhog = null;
if (!isPublish)
{
    mailhog = builder.AddContainer("mailhog", "mailhog/mailhog")
        .WithHttpEndpoint(port: 8025, targetPort: 8025, name: "ui")
        .WithEndpoint(port: 1025, targetPort: 1025, name: "smtp", scheme: "tcp");
}

// --- Keycloak ---
var keycloak = builder.AddKeycloak("keycloak", port: 8080)
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", kcAdminUsername)
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", kcAdminPassword);

if (!isPublish)
{
    // Local dev: H2 volume (persists between runs) + realm import via bind-mount + theme hot-reload.
    // KC scans /opt/keycloak/themes/<name>/ for themes; bind-mount our source dir so CSS edits
    // hot-reload without rebuilding the container. Dev cookie-jar pollution on localhost can exceed
    // the Quarkus default max header size → HTTP 431; QUARKUS_HTTP_LIMITS_MAX_HEADER_SIZE fixes it.
    keycloak
        .WithDataVolume()
        .WithRealmImport("../../infra/keycloak")
        .WaitFor(mailhog!)
        .WithBindMount("../../infra/keycloak/themes/agentos", "/opt/keycloak/themes/agentos")
        .WithEnvironment("KC_SPI_THEME_STATIC_MAX_AGE", "-1")
        .WithEnvironment("KC_SPI_THEME_CACHE_THEMES", "false")
        .WithEnvironment("KC_SPI_THEME_CACHE_TEMPLATES", "false")
        .WithEnvironment("QUARKUS_HTTP_LIMITS_MAX_HEADER_SIZE", "64K");
}
else
{
    // Cloud: custom image (infra/keycloak/Dockerfile) bakes realm JSON + theme into the image so
    // bind-mounts aren't needed. KC_PROXY=edge trusts the ACA TLS-terminating ingress X-Forwarded-*
    // headers. KC_HOSTNAME_STRICT=false lets KC auto-detect its public URL from requests on the first
    // deploy (before KC_HOSTNAME is set); set KC_HOSTNAME once you have the stable ACA FQDN.
    keycloak
        .WithDockerfile("../../infra/keycloak")
        .WithEnvironment("KC_PROXY", "edge")
        .WithEnvironment("KC_HOSTNAME_STRICT", "false");

    // Durable Postgres backend — only activate once KC DB params are set (step 2 of cloud bootstrap).
    // Without these, KC starts with H2 (ephemeral) which is fine for the initial provision round.
    var kcDbUrlValue = builder.Configuration["Parameters:KeycloakDbUrl"];
    if (!string.IsNullOrEmpty(kcDbUrlValue))
    {
        keycloak
            .WithEnvironment("KC_DB", "postgres")
            .WithEnvironment("KC_DB_URL", kcDbUrl)
            .WithEnvironment("KC_DB_USERNAME", kcDbUsername)
            .WithEnvironment("KC_DB_PASSWORD", kcDbPassword);
    }

    // Stable public hostname — set after first provision so issuer + redirect_uri are consistent.
    var kcHostnameValue = builder.Configuration["Parameters:KeycloakHostname"];
    if (!string.IsNullOrEmpty(kcHostnameValue))
    {
        keycloak.WithEnvironment("KC_HOSTNAME", kcHostname);
    }
}

// --- API ---
var api = builder.AddProject<Projects.AgentOs_Api>("api")
    .WithReference(db).WaitFor(db)
    .WithReference(keycloak).WaitFor(keycloak)
    .WithEnvironment("Auth__Keycloak__Authority", RealmAuthority(keycloak.GetEndpoint("http")))
    .WithEnvironment("Auth__Keycloak__Audience", "agentic-api")
    .WithEnvironment("Auth__Keycloak__Admin__BaseUrl", keycloak.GetEndpoint("http"))
    .WithEnvironment("Auth__Keycloak__Admin__Realm", "agentic")
    .WithEnvironment("Auth__Keycloak__Admin__Username", kcAdminUsername)
    .WithEnvironment("Auth__Keycloak__Admin__Password", kcAdminPassword)
    .WithEnvironment("Auth__Keycloak__Admin__ClientId", "admin-cli")
    .WithEnvironment("Email__From", "noreply@agentic.local")
    .WithEnvironment("Email__FromName", "AgentOS")
    .WithEnvironment("Email__SmtpHost", smtpHost)
    .WithEnvironment("Email__SmtpPort", smtpPort);

if (!isPublish) { api.WaitFor(mailhog!); }

// The Web must be reachable at EXACTLY https://localhost:5180 in run mode — that string is hard-wired
// into the Keycloak realm's redirectUris, so any other scheme/port/origin breaks OIDC login. Make that
// binding the single source of truth, with no possibility of a port clash:
//   • launchProfileName: null     — ignore launchSettings (its "http" profile pins http://localhost:5180,
//                                    which otherwise wins and makes Kestrel serve plain HTTP → the
//                                    browser gets ERR_SSL_PROTOCOL_ERROR and OIDC builds a http://
//                                    redirect_uri the realm rejects ('Invalid parameter: redirect_uri').
//   • isProxied: false + port==targetPort  — Kestrel binds https://localhost:5180 DIRECTLY (no Aspire
//                                    reverse-proxy in front), so nothing else contends for 5180 and the
//                                    request origin the app sees IS https://localhost:5180 → the OIDC
//                                    redirect_uri matches the realm with no forwarded-header juggling.
var web = builder.AddProject<Projects.AgentOs_Web>("web", launchProfileName: null)
    .WithHttpsEndpoint(port: 5180, targetPort: 5180, name: "https", isProxied: false)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", isPublish ? "Production" : "Development")
    .WithEnvironment("Auth__DevAutoLogin", "false")
    .WithReference(db).WaitFor(db)
    .WithReference(keycloak).WaitFor(keycloak)
    .WithEnvironment("Auth__Keycloak__Authority", RealmAuthority(keycloak.GetEndpoint("http")))
    .WithEnvironment("Auth__Keycloak__Audience", "agentic-api")
    .WithEnvironment("Auth__Keycloak__ClientId", "agentic-web")
    .WithEnvironment("Auth__Keycloak__ClientSecret", kcWebClientSecret)
    .WithEnvironment("Auth__Keycloak__Admin__BaseUrl", keycloak.GetEndpoint("http"))
    .WithEnvironment("Auth__Keycloak__Admin__Realm", "agentic")
    .WithEnvironment("Auth__Keycloak__Admin__Username", kcAdminUsername)
    .WithEnvironment("Auth__Keycloak__Admin__Password", kcAdminPassword)
    .WithEnvironment("Auth__Keycloak__Admin__ClientId", "admin-cli")
    .WithEnvironment("Email__From", "noreply@agentic.local")
    .WithEnvironment("Email__FromName", "AgentOS")
    .WithEnvironment("Email__SmtpHost", smtpHost)
    .WithEnvironment("Email__SmtpPort", smtpPort);

if (!isPublish) { web.WaitFor(mailhog!); }

builder.Build().Run();

// Build the Keycloak realm authority (<kc-url>/realms/agentic) as a deferred ReferenceExpression.
// Uses the explicit builder (AppendFormatted + AppendLiteral) rather than an interpolated string so the
// endpoint URL is captured as an IValueProvider — not stringified via Object.ToString() (cs/call-to-object-tostring).
static ReferenceExpression RealmAuthority(EndpointReference http)
{
    var b = new ReferenceExpressionBuilder();
    b.AppendFormatted(http.Property(EndpointProperty.Url));
    b.AppendLiteral("/realms/agentic");
    return b.Build();
}
