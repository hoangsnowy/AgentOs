// EF Core DbContext for the Tenants module. Owns `tenants.tenants` (admin-scoped registry, no
// global query filter — every admin endpoint guards on the Admin policy).

using AgenticSdlc.Modules.Tenants.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgenticSdlc.Modules.Tenants.Persistence;

internal sealed class TenantsDbContext : DbContext
{
    public TenantsDbContext(DbContextOptions<TenantsDbContext> options)
        : base(options)
    {
    }

    public DbSet<TenantEntity> Tenants => Set<TenantEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema("tenants");

        modelBuilder.Entity<TenantEntity>(e =>
        {
            e.ToTable("tenants");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(256).IsRequired();
        });
    }
}
