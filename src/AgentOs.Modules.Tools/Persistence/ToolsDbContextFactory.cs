// Design-time factory so `dotnet ef migrations` can construct the context outside the host DI.
// Mirrors the other modules' factories.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace AgentOs.Modules.Tools.Persistence;

public sealed class ToolsDbContextFactory : IDesignTimeDbContextFactory<ToolsDbContext>
{
    public ToolsDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var cs = config.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Database=agentos;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<ToolsDbContext>()
            .UseNpgsql(cs, npg => npg.MigrationsHistoryTable("__EFMigrationsHistory", schema: "tools"))
            .Options;

        return new ToolsDbContext(options);
    }
}
