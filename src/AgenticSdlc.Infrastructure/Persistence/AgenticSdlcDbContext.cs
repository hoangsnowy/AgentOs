// EF Core DbContext for the persistence layer (Postgres). 3 tables:
// pipeline_runs (run + artifact jsonb), run_metrics (1 LLM call), orchestrations (Agent Studio state).
using AgenticSdlc.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgenticSdlc.Infrastructure.Persistence;

internal sealed class AgenticSdlcDbContext(DbContextOptions<AgenticSdlcDbContext> options) : DbContext(options)
{
    public DbSet<PipelineRunEntity> PipelineRuns => Set<PipelineRunEntity>();

    public DbSet<RunMetricEntity> RunMetrics => Set<RunMetricEntity>();

    public DbSet<OrchestrationEntity> Orchestrations => Set<OrchestrationEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<PipelineRunEntity>(e =>
        {
            e.ToTable("pipeline_runs");
            e.HasKey(x => x.Id);
            e.Property(x => x.UserStoryText).IsRequired();
            e.Property(x => x.Status).HasMaxLength(32).IsRequired();
            e.Property(x => x.TotalCostUsd).HasPrecision(18, 6);
            e.Property(x => x.ResultJson).HasColumnType("jsonb").IsRequired();
            e.HasIndex(x => x.CreatedAtUtc);
            e.HasMany(x => x.Metrics)
             .WithOne()
             .HasForeignKey(m => m.RunId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RunMetricEntity>(e =>
        {
            e.ToTable("run_metrics");
            e.HasKey(x => x.Id);
            e.Property(x => x.KcId).HasMaxLength(32).IsRequired();
            e.Property(x => x.AgentName).HasMaxLength(64).IsRequired();
            e.Property(x => x.Model).HasMaxLength(128).IsRequired();
            e.Property(x => x.Provider).HasMaxLength(64).IsRequired();
            e.Property(x => x.CostUsd).HasPrecision(18, 6);
            e.HasIndex(x => x.RunId);
        });

        modelBuilder.Entity<OrchestrationEntity>(e =>
        {
            e.ToTable("orchestrations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(128);
            e.Property(x => x.Name).HasMaxLength(256).IsRequired();
            e.Property(x => x.DefinitionJson).HasColumnType("jsonb").IsRequired();
        });
    }
}
