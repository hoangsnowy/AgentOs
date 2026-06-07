// EF Core DbContext that stores the ASP.NET Core DataProtection key ring in Postgres
// (`config.data_protection_keys`). Backing both hosts with ONE durable, shared key ring is what makes
// encrypted tenant secrets (AppConfig) + OIDC correlation/nonce + auth cookies survive a restart/scale
// and decrypt across the Api ↔ Web processes. Distinct migrations-history table so it co-exists with
// AppConfigDbContext in the same `config` schema.

using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AgentOs.Modules.AppConfig.Persistence;

internal sealed class DataProtectionDbContext : DbContext, IDataProtectionKeyContext
{
    public DataProtectionDbContext(DbContextOptions<DataProtectionDbContext> options)
        : base(options)
    {
    }

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema("config");
        modelBuilder.Entity<DataProtectionKey>(e =>
        {
            e.ToTable("data_protection_keys");
            e.HasKey(x => x.Id);
        });
    }
}
