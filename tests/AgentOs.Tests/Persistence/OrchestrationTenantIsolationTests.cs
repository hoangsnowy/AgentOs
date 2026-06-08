// E — orchestration persistence is tenant-explicit (a Blazor circuit dispatches it on a Task.Run thread
// with no HttpContext, so the ambient tenant would be wrong). These prove the *ForTenant write/delete
// overloads isolate by the explicit tenant: a write is stamped with the caller's tenant, and a tenant
// cannot delete another tenant's row by id (the Id is the global PK, so a cross-tenant id is guessable —
// the explicit tenant predicate is what blocks the delete). EF Core InMemory; assertions bypass the filter.

using AgentOs.Modules.Pipeline.Persistence;
using AgentOs.Modules.Pipeline.Persistence.Repositories;
using AgentOs.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Persistence;

public sealed class OrchestrationTenantIsolationTests
{
    private sealed class FakeTenant(string id) : ITenantContext
    {
        public string TenantId { get; } = id;
        public string? UserId => "u";
        public string? UserName => "u";
        public IReadOnlyList<string> Roles { get; } = new[] { "admin" };
        public bool IsAuthenticated => true;
        public bool IsAdmin => true;
    }

    private static DbContextOptions<PipelineDbContext> SharedOptions() =>
        new DbContextOptionsBuilder<PipelineDbContext>()
            .UseInMemoryDatabase($"orch-isolation-{Guid.NewGuid()}")
            .Options;

    private static OrchestrationRecord Rec(string id, string name) =>
        new(id, name, null, "{}", DateTimeOffset.UtcNow);

    [Fact]
    public async Task UpsertAndDeleteForTenant_IsolateBetweenTenants()
    {
        var options = SharedOptions();
        var alice = new FakeTenant("alice");
        var bob = new FakeTenant("bob");

        await using (var db = new PipelineDbContext(options, alice))
        {
            await new OrchestrationRepository(db, alice).UpsertForTenantAsync("alice", Rec("alice-g", "Alice graph"));
        }
        await using (var db = new PipelineDbContext(options, bob))
        {
            await new OrchestrationRepository(db, bob).UpsertForTenantAsync("bob", Rec("bob-g", "Bob graph"));
        }

        // Each write is stamped with the caller's tenant.
        await using (var db = new PipelineDbContext(options, alice))
        {
            var all = await db.Orchestrations.IgnoreQueryFilters().ToListAsync();
            all.Count.ShouldBe(2);
            all.ShouldContain(o => o.Id == "alice-g" && o.TenantId == "alice");
            all.ShouldContain(o => o.Id == "bob-g" && o.TenantId == "bob");
        }

        // Bob tries to delete ALICE's row by its (guessable, global-PK) id — the explicit tenant predicate
        // blocks it, so Alice's row survives.
        await using (var db = new PipelineDbContext(options, bob))
        {
            await new OrchestrationRepository(db, bob).DeleteForTenantAsync("bob", "alice-g");
        }
        await using (var db = new PipelineDbContext(options, alice))
        {
            (await db.Orchestrations.IgnoreQueryFilters().CountAsync()).ShouldBe(2, "Bob must not delete Alice's row");
        }

        // Bob deletes its OWN row — succeeds.
        await using (var db = new PipelineDbContext(options, bob))
        {
            await new OrchestrationRepository(db, bob).DeleteForTenantAsync("bob", "bob-g");
        }
        await using (var db = new PipelineDbContext(options, alice))
        {
            var remaining = await db.Orchestrations.IgnoreQueryFilters().ToListAsync();
            remaining.Count.ShouldBe(1);
            remaining[0].Id.ShouldBe("alice-g");
        }
    }
}
