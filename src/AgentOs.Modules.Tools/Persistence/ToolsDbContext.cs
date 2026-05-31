// M1 — Tools persistence (schema tools). One aggregate: durable tool-invocation evidence. No tenant
// global query filter — the IToolInvocationLog API is already tenant-explicit (ListRecentAsync takes a
// tenant id; appends carry the evidence's TenantId), so partitioning is by an indexed column.

using System;
using AgentOs.Modules.Tools.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgentOs.Modules.Tools.Persistence;

/// <summary>EF Core context for durable tool-invocation evidence (schema <c>tools</c>).</summary>
public sealed class ToolsDbContext : DbContext
{
    public ToolsDbContext(DbContextOptions<ToolsDbContext> options) : base(options) { }

    public DbSet<ToolInvocationEvidenceEntity> ToolInvocations => Set<ToolInvocationEvidenceEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema("tools");

        modelBuilder.Entity<ToolInvocationEvidenceEntity>(e =>
        {
            e.ToTable("tool_invocations");
            e.HasKey(x => x.Id);
            e.Property(x => x.CallId).IsRequired().HasMaxLength(128);
            e.Property(x => x.ToolName).IsRequired().HasMaxLength(128);
            e.Property(x => x.TenantId).IsRequired().HasMaxLength(64);
            e.Property(x => x.RunId).HasMaxLength(128);
            e.Property(x => x.SessionId).HasMaxLength(128);
            e.Property(x => x.IsError).IsRequired();
            e.Property(x => x.StartedUtc).IsRequired();
            e.Property(x => x.FinishedUtc).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.StartedUtc });
        });
    }
}
