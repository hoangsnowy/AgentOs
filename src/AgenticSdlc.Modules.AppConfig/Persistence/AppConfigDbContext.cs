// EF Core DbContext for the AppConfig module. Owns the `config.app_config` table — encrypted
// key/value settings, keyed by (TenantId, Key). No global tenant query filter: EfAppConfigStore is
// a singleton that supplies the current tenant explicitly on every call.

using AgenticSdlc.Modules.AppConfig.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgenticSdlc.Modules.AppConfig.Persistence;

internal sealed class AppConfigDbContext : DbContext
{
    public AppConfigDbContext(DbContextOptions<AppConfigDbContext> options)
        : base(options)
    {
    }

    public DbSet<AppConfigEntity> AppConfig => Set<AppConfigEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema("config");

        modelBuilder.Entity<AppConfigEntity>(e =>
        {
            e.ToTable("app_config");
            e.HasKey(x => new { x.TenantId, x.Key });
            e.Property(x => x.TenantId).HasMaxLength(64);
            e.Property(x => x.Key).HasMaxLength(256);
            e.Property(x => x.EncryptedValue).IsRequired();
        });
    }
}
