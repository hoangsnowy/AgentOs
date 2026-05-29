// Aspire AppHost — single F5 brings up Postgres + Keycloak + API + Web for local dev, and azd
// promotes Postgres to Azure Database for PostgreSQL flexible server in the cloud. The DB resource
// is named "DefaultConnection" so WithReference injects ConnectionStrings__DefaultConnection,
// matching AddPersistence in Infrastructure. Keycloak realm "agentic" is auto-imported from
// ./infra/keycloak; its HTTP URL is forwarded to api/web as Auth__Keycloak__Authority so
// AddJwtAuth(keycloak mode) wires the OIDC resource server without any hardcoded URL.
using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// Deploy (azd): a managed Azure Database for PostgreSQL flexible server.
// Local: runs as a Postgres container (data persisted via volume).
var db = builder.AddAzurePostgresFlexibleServer("postgres")
    .RunAsContainer(c => c.WithDataVolume())
    .AddDatabase("DefaultConnection", databaseName: "agentos");

// Keycloak — OIDC identity provider for multi-tenant auth (Epic D). Dev mode (embedded store)
// with the "agentic" realm auto-imported from ../../infra/keycloak. Data volume persists users
// and admin password across restarts so seeded users / per-tenant config survive `dotnet run`.
// Production swaps this for an external Keycloak (or any OIDC IdP) by overriding Auth:Keycloak.
var keycloak = builder.AddKeycloak("keycloak", port: 8080)
    .WithDataVolume()
    .WithRealmImport("../../infra/keycloak");

builder.AddProject<Projects.AgentOs_Api>("api")
    .WithReference(db).WaitFor(db)
    .WithReference(keycloak).WaitFor(keycloak)
    .WithEnvironment("Auth__Mode", "keycloak")
    .WithEnvironment("Auth__Keycloak__Authority",
        ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/agentic"))
    .WithEnvironment("Auth__Keycloak__Audience", "agentic-api")
    // Admin REST — POST /tenants and member-invite endpoints provision realm users via this.
    .WithEnvironment("Auth__Keycloak__Admin__BaseUrl",
        ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}"))
    .WithEnvironment("Auth__Keycloak__Admin__Realm", "agentic")
    .WithEnvironment("Auth__Keycloak__Admin__Username", "admin")
    .WithEnvironment("Auth__Keycloak__Admin__Password", "admin")
    .WithEnvironment("Auth__Keycloak__Admin__ClientId", "admin-cli");

builder.AddProject<Projects.AgentOs_Web>("web")
    .WithReference(db).WaitFor(db)
    .WithReference(keycloak).WaitFor(keycloak)
    .WithEnvironment("Auth__Mode", "keycloak")
    .WithEnvironment("Auth__Keycloak__Authority",
        ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/agentic"));

builder.Build().Run();
