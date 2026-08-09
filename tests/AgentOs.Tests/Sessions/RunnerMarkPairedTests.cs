// RunnerEntity documents "Pending → Paired (first successful connect) → Revoked", but nothing ever
// wrote "Paired": RemoteAgentHub.OnConnectedAsync registered the connection with the broker and left
// the row alone. A dev machine that had genuinely paired still read "Pending" in Board's Runners table
// forever — the UI reported a broken pairing that actually worked. These tests pin the transition and
// the one case that must NOT move: a revoked runner.

using System;
using System.Threading.Tasks;
using AgentOs.Modules.Sessions.Persistence;
using AgentOs.Modules.Sessions.Persistence.Entities;
using AgentOs.Modules.Sessions.Persistence.Repositories;
using AgentOs.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Sessions;

public class RunnerMarkPairedTests
{
    [Fact]
    public async Task MarkPairedAsync_PendingRunner_BecomesPairedAndStampsLastSeen()
    {
        var options = NewOptions();
        var tenant = new FixedTenant("t1");
        var id = Guid.NewGuid();
        await SeedAsync(options, tenant, id, status: "Pending");

        await using (var db = new SessionsDbContext(options, tenant))
        {
            var updated = await new EfRunnerDirectory(db).MarkPairedAsync(id);
            updated.ShouldBeTrue();
        }

        await using (var db = new SessionsDbContext(options, tenant))
        {
            var row = await db.Runners.IgnoreQueryFilters().FirstAsync(r => r.Id == id);
            row.Status.ShouldBe("Paired");
            row.LastSeenUtc.ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task MarkPairedAsync_RevokedRunner_StaysRevoked()
    {
        var options = NewOptions();
        var tenant = new FixedTenant("t1");
        var id = Guid.NewGuid();
        await SeedAsync(options, tenant, id, status: "Revoked");

        await using (var db = new SessionsDbContext(options, tenant))
        {
            var updated = await new EfRunnerDirectory(db).MarkPairedAsync(id);
            updated.ShouldBeFalse();
        }

        await using (var db = new SessionsDbContext(options, tenant))
        {
            var row = await db.Runners.IgnoreQueryFilters().FirstAsync(r => r.Id == id);
            row.Status.ShouldBe("Revoked");
        }
    }

    [Fact]
    public async Task MarkPairedAsync_ForeignTenantRunner_StillUpdates()
    {
        // The handshake carries no tenant (the token is the credential), so the update must bypass the
        // tenant query filter exactly as the lookup does — otherwise pairing silently no-ops for every
        // runner that does not belong to whatever tenant the ambient context happens to hold.
        var options = NewOptions();
        var id = Guid.NewGuid();
        await SeedAsync(options, new FixedTenant("owner-tenant"), id, status: "Pending");

        await using (var db = new SessionsDbContext(options, new FixedTenant("some-other-tenant")))
        {
            var updated = await new EfRunnerDirectory(db).MarkPairedAsync(id);
            updated.ShouldBeTrue();
        }

        await using (var db = new SessionsDbContext(options, new FixedTenant("owner-tenant")))
        {
            var row = await db.Runners.IgnoreQueryFilters().FirstAsync(r => r.Id == id);
            row.Status.ShouldBe("Paired");
        }
    }

    private static DbContextOptions<SessionsDbContext> NewOptions() =>
        new DbContextOptionsBuilder<SessionsDbContext>()
            .UseInMemoryDatabase($"runners-paired-{Guid.NewGuid()}")
            .Options;

    private static async Task SeedAsync(
        DbContextOptions<SessionsDbContext> options, ITenantContext tenant, Guid id, string status)
    {
        await using var db = new SessionsDbContext(options, tenant);
        db.Runners.Add(new RunnerEntity
        {
            Id = id,
            TenantId = tenant.TenantId,
            OwnerUserId = "user-1",
            Label = "dev box",
            TokenHash = "sha256$salt$hash",
            Status = status,
            CreatedAtUtc = DateTimeOffset.UnixEpoch,
        });
        await db.SaveChangesAsync();
    }

    private sealed class FixedTenant(string id) : ITenantContext
    {
        public string TenantId => id;
        public string? UserId => "user-1";
        public string? UserName => "user-1";
        public System.Collections.Generic.IReadOnlyList<string> Roles => Array.Empty<string>();
        public bool IsAuthenticated => true;
        public bool IsAdmin => false;
    }
}
