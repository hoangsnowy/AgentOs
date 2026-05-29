// EF Core DbContext for the persistence layer (Postgres). 5 tables:
// pipeline_runs (run + artifact jsonb), run_metrics (1 LLM call), orchestrations (Agent Studio
// state), app_config (encrypted KV), tenants (registry). Every tenant-owned table carries a
// TenantId column and a global query filter that reads ITenantContext.TenantId, so each request
// only ever sees rows owned by its own tenant. Writes stamp TenantId in the repos; reads are
// filtered automatically. The design-time factory wires a DefaultTenantContext stub so
// `dotnet ef migrations add` keeps working.
using AgenticSdlc.Application.Identity;
using AgenticSdlc.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgenticSdlc.Infrastructure.Persistence;

internal sealed class AgenticSdlcDbContext : DbContext
{
    private readonly ITenantContext _tenant;

    public AgenticSdlcDbContext(DbContextOptions<AgenticSdlcDbContext> options, ITenantContext tenant)
        : base(options)
    {
        _tenant = tenant;
    }

    public DbSet<PipelineRunEntity> PipelineRuns => Set<PipelineRunEntity>();

    public DbSet<RunMetricEntity> RunMetrics => Set<RunMetricEntity>();

    public DbSet<OrchestrationEntity> Orchestrations => Set<OrchestrationEntity>();

    public DbSet<AppConfigEntity> AppConfig => Set<AppConfigEntity>();

    public DbSet<TenantEntity> Tenants => Set<TenantEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<AppConfigEntity>(e =>
        {
            e.ToTable("app_config");
            e.HasKey(x => new { x.TenantId, x.Key });
            e.Property(x => x.TenantId).HasMaxLength(64);
            e.Property(x => x.Key).HasMaxLength(256);
            e.Property(x => x.EncryptedValue).IsRequired();
            // app_config is queried directly by EfAppConfigStore which is a singleton — its read
            // path supplies the tenant id explicitly, so no global filter here. (Adding one would
            // require an ITenantContext scope around every store call.)
        });

        modelBuilder.Entity<PipelineRunEntity>(e =>
        {
            e.ToTable("pipeline_runs");
            e.HasKey(x => x.Id);
            e.Property(x => x.TenantId).HasMaxLength(64).IsRequired();
            e.Property(x => x.UserStoryText).IsRequired();
            e.Property(x => x.Status).HasMaxLength(32).IsRequired();
            e.Property(x => x.TotalCostUsd).HasPrecision(18, 6);
            e.Property(x => x.ResultJson).HasColumnType("jsonb").IsRequired();
            e.HasIndex(x => x.CreatedAtUtc);
            e.HasIndex(x => x.TenantId);
            e.HasMany(x => x.Metrics)
             .WithOne()
             .HasForeignKey(m => m.RunId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        modelBuilder.Entity<RunMetricEntity>(e =>
        {
            e.ToTable("run_metrics");
            e.HasKey(x => x.Id);
            e.Property(x => x.TenantId).HasMaxLength(64).IsRequired();
            e.Property(x => x.KcId).HasMaxLength(32).IsRequired();
            e.Property(x => x.AgentName).HasMaxLength(64).IsRequired();
            e.Property(x => x.Model).HasMaxLength(128).IsRequired();
            e.Property(x => x.Provider).HasMaxLength(64).IsRequired();
            e.Property(x => x.CostUsd).HasPrecision(18, 6);
            e.HasIndex(x => x.RunId);
            e.HasIndex(x => x.TenantId);
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        modelBuilder.Entity<OrchestrationEntity>(e =>
        {
            e.ToTable("orchestrations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(128);
            e.Property(x => x.TenantId).HasMaxLength(64).IsRequired();
            e.Property(x => x.Name).HasMaxLength(256).IsRequired();
            e.Property(x => x.DefinitionJson).HasColumnType("jsonb").IsRequired();
            e.HasIndex(x => x.TenantId);
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        modelBuilder.Entity<TenantEntity>(e =>
        {
            e.ToTable("tenants");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(256).IsRequired();
            // No filter: the tenant registry is read by admin endpoints which span all tenants.
        });
    }
}
