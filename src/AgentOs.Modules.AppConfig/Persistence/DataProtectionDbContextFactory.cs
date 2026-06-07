// Design-time factory for `dotnet ef migrations add` on DataProtectionDbContext. Mirrors
// AppConfigDbContextFactory: reads ConnectionStrings__DefaultConnection, falls back to localhost.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AgentOs.Modules.AppConfig.Persistence;

internal sealed class DataProtectionDbContextFactory : IDesignTimeDbContextFactory<DataProtectionDbContext>
{
    public DataProtectionDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=agentos;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<DataProtectionDbContext>()
            .UseNpgsql(connectionString, npg =>
                npg.MigrationsHistoryTable("__DataProtectionMigrationsHistory", schema: "config"))
            .Options;

        return new DataProtectionDbContext(options);
    }
}
