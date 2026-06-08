// Regression lock for C1 (Workspaces side): WorkspacesDbContext built its tenant query filter from a
// LOCAL captured in OnModelCreating, which EF bakes into the once-per-type cached model → frozen to the
// first tenant. The direct filter-path asserts below fail with that bug and pass once the filter
// references the instance member (_tenant). One [Fact], single-threaded, the only tests that build the
// WorkspacesDbContext model. EF Core InMemory — query filters work identically there.

using AgentOs.Modules.Workspaces.Persistence;
using AgentOs.Modules.Workspaces.Persistence.Entities;
using AgentOs.Modules.Workspaces.Persistence.Repositories;
using AgentOs.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Persistence;

public sealed class WorkspacesTenantIsolationTests
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

    private static DbContextOptions<WorkspacesDbContext> SharedOptions() =>
        new DbContextOptionsBuilder<WorkspacesDbContext>()
            .UseInMemoryDatabase($"workspaces-isolation-{Guid.NewGuid()}")
            .Options;

    [Fact]
    public async Task QueryFilter_IsolatesWorkspaces_BetweenTenants()
    {
        var options = SharedOptions();
        var alice = new FakeTenant("alice");
        var bob = new FakeTenant("bob");

        var aliceWs = Guid.NewGuid();
        var bobWs = Guid.NewGuid();

        // Seed cross-tenant rows. Use the alice context first so the (buggy) model would freeze to alice.
        await using (var db = new WorkspacesDbContext(options, alice))
        {
            db.Workspaces.Add(Workspace(aliceWs, "alice"));
            await db.SaveChangesAsync();
        }
        await using (var db = new WorkspacesDbContext(options, bob))
        {
            db.Workspaces.Add(Workspace(bobWs, "bob"));
            await db.SaveChangesAsync();
        }

        // Filter path (relies purely on the global filter — the true freeze lock).
        await using (var db = new WorkspacesDbContext(options, alice))
        {
            (await db.Workspaces.Select(w => w.Id).ToListAsync()).ShouldBe(new[] { aliceWs });
        }
        await using (var db = new WorkspacesDbContext(options, bob))
        {
            (await db.Workspaces.Select(w => w.Id).ToListAsync()).ShouldBe(new[] { bobWs });
        }

        // Mutate-by-id path: bob can remove its own board but not Alice's. Under the freeze, bob's OWN
        // remove also fails (frozen-alice filter blocks bob's row), so this catches the bug too.
        await using (var db = new WorkspacesDbContext(options, bob))
        {
            var repo = new WorkspaceRepository(db, bob);
            (await repo.RemoveAsync(aliceWs)).ShouldBeFalse("Bob must not remove Alice's workspace");
            (await repo.RemoveAsync(bobWs)).ShouldBeTrue("Bob must be able to remove its own workspace");
        }

        // Alice's board survives Bob's attempt.
        await using (var db = new WorkspacesDbContext(options, alice))
        {
            (await db.Workspaces.AnyAsync(w => w.Id == aliceWs)).ShouldBeTrue("Alice's workspace must survive Bob's attempt");
        }
    }

    private static WorkspaceEntity Workspace(Guid id, string tenant) => new()
    {
        Id = id,
        TenantId = tenant,
        Name = "Board",
        ProjectOwner = "owner",
        ProjectScope = "user",
        CredentialRef = "cred-ref",
        Status = "Connected",
        CreatedAtUtc = DateTimeOffset.UtcNow,
    };
}
