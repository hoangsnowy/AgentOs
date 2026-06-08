// Regression lock for C1: SessionsDbContext built its tenant query filter from a LOCAL captured in
// OnModelCreating. EF caches the compiled model once per context type, so a closure local is funcletized
// to a constant and freezes to whichever tenant first builds the model. The direct filter-path asserts
// below fail with that bug and pass once the filter references the instance member (_tenant). One [Fact],
// single-threaded inside, so the process-wide model cache is not raced (and these are the only tests that
// build the SessionsDbContext model). Uses EF Core InMemory — query filters work identically there.

using AgentOs.Modules.Sessions.Persistence;
using AgentOs.Modules.Sessions.Persistence.Entities;
using AgentOs.Modules.Sessions.Persistence.Repositories;
using AgentOs.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Persistence;

public sealed class SessionsTenantIsolationTests
{
    private sealed class FakeTenant(string id) : ITenantContext
    {
        public string TenantId { get; } = id;
        public string? UserId => "test";
        public string? UserName => "test";
        public IReadOnlyList<string> Roles { get; } = new[] { "admin" };
        public bool IsAuthenticated => true;
        public bool IsAdmin => true;
    }

    private static DbContextOptions<SessionsDbContext> SharedOptions() =>
        new DbContextOptionsBuilder<SessionsDbContext>()
            .UseInMemoryDatabase($"sessions-isolation-{Guid.NewGuid()}")
            .Options;

    [Fact]
    public async Task QueryFilter_IsolatesSessionsAndRunners_BetweenTenants()
    {
        var options = SharedOptions();
        var alice = new FakeTenant("alice");
        var bob = new FakeTenant("bob");

        var aliceSession = Guid.NewGuid();
        var bobSession = Guid.NewGuid();
        var aliceRunner = Guid.NewGuid();
        var bobRunner = Guid.NewGuid();

        // Seed cross-tenant rows. Add() bypasses the query filter, so both tenants' rows land in the one
        // shared store. Use the alice context FIRST so the (buggy) model would freeze to "alice"; the
        // both-direction asserts below catch the freeze regardless of which tenant froze.
        await using (var db = new SessionsDbContext(options, alice))
        {
            db.Sessions.Add(Session(aliceSession, "alice"));
            db.Runners.Add(Runner(aliceRunner, "alice"));
            await db.SaveChangesAsync();
        }
        await using (var db = new SessionsDbContext(options, bob))
        {
            db.Sessions.Add(Session(bobSession, "bob"));
            db.Runners.Add(Runner(bobRunner, "bob"));
            await db.SaveChangesAsync();
        }

        // Filter path (relies purely on the global filter — the true freeze lock): each tenant sees only
        // its own rows.
        await using (var db = new SessionsDbContext(options, alice))
        {
            (await db.Sessions.Select(s => s.Id).ToListAsync()).ShouldBe(new[] { aliceSession });
            (await db.Runners.Select(r => r.Id).ToListAsync()).ShouldBe(new[] { aliceRunner });
        }
        await using (var db = new SessionsDbContext(options, bob))
        {
            (await db.Sessions.Select(s => s.Id).ToListAsync()).ShouldBe(new[] { bobSession });
            (await db.Runners.Select(r => r.Id).ToListAsync()).ShouldBe(new[] { bobRunner });
        }

        // Mutate-by-id path: bob can act on its own rows but not Alice's. Under the freeze, bob's OWN
        // mutate also fails (the frozen-alice filter blocks bob's row), so this catches the bug too.
        await using (var db = new SessionsDbContext(options, bob))
        {
            var sessions = new SessionRepository(db, bob);
            var runners = new RunnerRepository(db, bob);
            (await sessions.CloseAsync(aliceSession, DateTimeOffset.UtcNow)).ShouldBeFalse("Bob must not close Alice's session");
            (await runners.SetStatusAsync(aliceRunner, "Revoked")).ShouldBeFalse("Bob must not mutate Alice's runner");
            (await sessions.CloseAsync(bobSession, DateTimeOffset.UtcNow)).ShouldBeTrue("Bob must be able to close its own session");
            (await runners.SetStatusAsync(bobRunner, "Revoked")).ShouldBeTrue("Bob must be able to mutate its own runner");
        }

        // Alice's rows are untouched by Bob's attempts.
        await using (var db = new SessionsDbContext(options, alice))
        {
            (await db.Sessions.FirstAsync(s => s.Id == aliceSession)).Status.ShouldBe("Draft");
            (await db.Runners.FirstAsync(r => r.Id == aliceRunner)).Status.ShouldBe("Pending");
        }
    }

    private static RemoteSessionEntity Session(Guid id, string tenant) => new()
    {
        Id = id,
        TenantId = tenant,
        WorkspaceId = Guid.NewGuid(),
        MemberUserId = "member",
        Title = "work",
        Status = "Draft",
        CreatedAtUtc = DateTimeOffset.UtcNow,
    };

    private static RunnerEntity Runner(Guid id, string tenant) => new()
    {
        Id = id,
        TenantId = tenant,
        OwnerUserId = "member",
        Label = "dev-machine",
        TokenHash = "sha256$salt$hash",
        Status = "Pending",
        CreatedAtUtc = DateTimeOffset.UtcNow,
    };
}
