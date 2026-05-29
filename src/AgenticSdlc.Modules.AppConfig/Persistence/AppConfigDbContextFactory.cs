// Design-time factory for `dotnet ef migrations add` on AppConfigDbContext. Reads the connection
// string from the ConnectionStrings__DefaultConnection env var, falls back to localhost dev defaults.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AgenticSdlc.Modules.AppConfig.Persistence;

internal sealed class AppConfigDbContextFactory : IDesignTimeDbContextFactory<AppConfigDbContext>
{
    public AppConfigDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=agentic_sdlc;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<AppConfigDbContext>()
            .UseNpgsql(connectionString, npg => npg.MigrationsHistoryTable("__EFMigrationsHistory", schema: "config"))
            .Options;

        return new AppConfigDbContext(options);
    }
}
