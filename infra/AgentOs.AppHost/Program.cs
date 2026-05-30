// Aspire AppHost — single F5 brings up Postgres + Keycloak + API + Web for local dev. azd promotes
// Postgres to Azure Database for PostgreSQL flexible server in the cloud. The DB resource is named
// "DefaultConnection" so WithReference injects ConnectionStrings__DefaultConnection. Keycloak realm
// "agentic" is auto-imported from ./infra/keycloak; its HTTP URL is forwarded as
// Auth__Keycloak__Authority so the API (JWT bearer) and the Web (OIDC code flow) both wire against
// it without any hardcoded URL. Web pins the HTTP endpoint to 5180 to match the realm's
// `agentic-web` client redirectUris.
using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var db = builder.AddAzurePostgresFlexibleServer("postgres")
    .RunAsContainer(c => c.WithDataVolume())
    .AddDatabase("DefaultConnection", databaseName: "agentos");

// Force a deterministic admin user so the KeycloakAdminClient and the realm-provisioning flow
// can rely on the master-realm `admin / admin` credentials in dev. Production uses a parameter.
var keycloak = builder.AddKeycloak("keycloak", port: 8080)
    .WithDataVolume()
    .WithRealmImport("../../infra/keycloak")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", "admin");

builder.AddProject<Projects.AgentOs_Api>("api")
    .WithReference(db).WaitFor(db)
    .WithReference(keycloak).WaitFor(keycloak)
    .WithEnvironment("Auth__Keycloak__Authority",
        ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/agentic"))
    .WithEnvironment("Auth__Keycloak__Audience", "agentic-api")
    // Admin REST — /tenants and member-invite endpoints provision realm users via this.
    .WithEnvironment("Auth__Keycloak__Admin__BaseUrl",
        ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}"))
    .WithEnvironment("Auth__Keycloak__Admin__Realm", "agentic")
    .WithEnvironment("Auth__Keycloak__Admin__Username", "admin")
    .WithEnvironment("Auth__Keycloak__Admin__Password", "admin")
    .WithEnvironment("Auth__Keycloak__Admin__ClientId", "admin-cli");

builder.AddProject<Projects.AgentOs_Web>("web")
    .WithHttpEndpoint(port: 5180, name: "http")
    .WithReference(db).WaitFor(db)
    .WithReference(keycloak).WaitFor(keycloak)
    .WithEnvironment("Auth__Keycloak__Authority",
        ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/agentic"))
    .WithEnvironment("Auth__Keycloak__Audience", "agentic-api")
    .WithEnvironment("Auth__Keycloak__ClientId", "agentic-web")
    .WithEnvironment("Auth__Keycloak__ClientSecret", "agentic-web-dev-secret")
    // Web provisions Keycloak users for the public sign-up form, so it also needs admin creds.
    .WithEnvironment("Auth__Keycloak__Admin__BaseUrl",
        ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}"))
    .WithEnvironment("Auth__Keycloak__Admin__Realm", "agentic")
    .WithEnvironment("Auth__Keycloak__Admin__Username", "admin")
    .WithEnvironment("Auth__Keycloak__Admin__Password", "admin")
    .WithEnvironment("Auth__Keycloak__Admin__ClientId", "admin-cli");

builder.Build().Run();
