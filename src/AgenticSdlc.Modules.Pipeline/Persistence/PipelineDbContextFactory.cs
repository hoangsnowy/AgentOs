// Design-time factory for `dotnet ef migrations add` on PipelineDbContext. Stubs ITenantContext
// so OnModelCreating's HasQueryFilter doesn't crash during scaffolding.

using AgenticSdlc.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AgenticSdlc.Modules.Pipeline.Persistence;

internal sealed class PipelineDbContextFactory : IDesignTimeDbContextFactory<PipelineDbContext>
{
    public PipelineDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=agentic_sdlc;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<PipelineDbContext>()
            .UseNpgsql(connectionString, npg => npg.MigrationsHistoryTable("__EFMigrationsHistory", schema: "pipeline"))
            .Options;

        return new PipelineDbContext(options, new DesignTimeTenantContext());
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public string TenantId => ITenantContext.DefaultTenantId;
        public string? UserId => null;
        public string? UserName => null;
        public System.Collections.Generic.IReadOnlyList<string> Roles { get; } = System.Array.Empty<string>();
        public bool IsAuthenticated => false;
        public bool IsAdmin => false;
    }
}
