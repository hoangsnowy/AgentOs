// Aspire AppHost — orchestrates Postgres + API + Web for local dev (single F5) and azd deploy.
// The Postgres database resource is named "DefaultConnection" so WithReference injects
// ConnectionStrings__DefaultConnection, matching AddPersistence in Infrastructure.
var builder = DistributedApplication.CreateBuilder(args);

// Deploy (azd): a managed Azure Database for PostgreSQL flexible server.
// Local: runs as a Postgres container (data persisted via volume).
var db = builder.AddAzurePostgresFlexibleServer("postgres")
    .RunAsContainer(c => c.WithDataVolume())
    .AddDatabase("DefaultConnection", databaseName: "agentic_sdlc");

builder.AddProject<Projects.AgenticSdlc_Api>("api")
    .WithReference(db)
    .WaitFor(db);

builder.AddProject<Projects.AgenticSdlc_Web>("web")
    .WithReference(db)
    .WaitFor(db);

builder.Build().Run();
