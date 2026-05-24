// Design-time factory for `dotnet ef migrations add`. NOT used at runtime.
// The connection string comes from env (CI/local) or defaults to localhost for generating migrations.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AgenticSdlc.Infrastructure.Persistence;

internal sealed class AgenticSdlcDbContextFactory : IDesignTimeDbContextFactory<AgenticSdlcDbContext>
{
    public AgenticSdlcDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=agentic_sdlc;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<AgenticSdlcDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AgenticSdlcDbContext(options);
    }
}
