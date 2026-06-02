// M3 — Sessions persistence (schema sessions). Two aggregates: Runner (a paired machine) and
// RemoteSession (a member × workspace unit of work). Both tenant-scoped via a global query filter on
// TenantId mirrored from ITenantContext — same pattern as WorkspacesDbContext / PipelineDbContext.
// NOTE: the pairing handshake reads a runner with the filter bypassed (see EfRunnerDirectory) because
// the connecting runner has no tenant context yet; the token hash is the credential.

using System;
using AgentOs.Modules.Sessions.Persistence.Entities;
using AgentOs.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;

namespace AgentOs.Modules.Sessions.Persistence;

/// <summary>EF Core context for session + runner persistence (schema <c>sessions</c>).</summary>
public sealed class SessionsDbContext : DbContext
{
    private readonly ITenantContext? _tenant;

    public SessionsDbContext(DbContextOptions<SessionsDbContext> options, ITenantContext? tenant = null)
        : base(options)
    {
        _tenant = tenant;
    }

    public DbSet<RunnerEntity> Runners => Set<RunnerEntity>();
    public DbSet<RemoteSessionEntity> Sessions => Set<RemoteSessionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema("sessions");

        var tenantId = _tenant?.TenantId ?? string.Empty;

        modelBuilder.Entity<RunnerEntity>(e =>
        {
            e.ToTable("runners");
            e.HasKey(x => x.Id);
            e.Property(x => x.TenantId).IsRequired().HasMaxLength(64);
            e.Property(x => x.OwnerUserId).IsRequired().HasMaxLength(256);
            e.Property(x => x.Label).IsRequired().HasMaxLength(200);
            e.Property(x => x.TokenHash).IsRequired().HasMaxLength(256);
            e.Property(x => x.Status).IsRequired().HasMaxLength(32);
            e.Property(x => x.CreatedAtUtc).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.OwnerUserId });
            e.HasQueryFilter(x => x.TenantId == tenantId);
        });

        modelBuilder.Entity<RemoteSessionEntity>(e =>
        {
            e.ToTable("sessions");
            e.HasKey(x => x.Id);
            e.Property(x => x.TenantId).IsRequired().HasMaxLength(64);
            e.Property(x => x.MemberUserId).IsRequired().HasMaxLength(256);
            e.Property(x => x.Title).IsRequired().HasMaxLength(200);
            e.Property(x => x.Status).IsRequired().HasMaxLength(32);
            e.Property(x => x.CreatedAtUtc).IsRequired();
            e.Property(x => x.RepoOwner).HasMaxLength(256);
            e.Property(x => x.RepoName).HasMaxLength(256);
            e.Property(x => x.RepoDefaultBranch).HasMaxLength(256);
            e.HasIndex(x => new { x.TenantId, x.CreatedAtUtc });
            e.HasQueryFilter(x => x.TenantId == tenantId);
        });
    }
}
