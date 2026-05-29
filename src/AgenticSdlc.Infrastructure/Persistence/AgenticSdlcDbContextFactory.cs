// Design-time factory for `dotnet ef migrations add`. NOT used at runtime. Passes a stub
// ITenantContext so the DbContext ctor — which now requires one for global query filters — can be
// instantiated by the EF tooling without spinning the full DI graph.
using AgenticSdlc.Infrastructure.Identity;
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

        return new AgenticSdlcDbContext(options, new DefaultTenantContext());
    }
}
