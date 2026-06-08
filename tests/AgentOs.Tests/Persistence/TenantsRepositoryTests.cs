// Smoke tests for the tenant registry — the table has no global filter, so admins see every
// tenant across the realm.

using AgentOs.SharedKernel.Identity;
using AgentOs.Modules.Tenants;
using AgentOs.Tests.Identity;
using AgentOs.Modules.Tenants.Persistence;
using AgentOs.Modules.Tenants.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Persistence;

public sealed class TenantsRepositoryTests
{
    private static DbContextOptions<TenantsDbContext> NewOptions() =>
        new DbContextOptionsBuilder<TenantsDbContext>()
            .UseInMemoryDatabase($"tenants-{Guid.NewGuid()}")
            .Options;

    private static readonly TestTenantContext Tenant = new();
    private static readonly string[] ExpectedIds = ["acme", "globex"];

    private static TenantsDbContext NewDb(DbContextOptions<TenantsDbContext> options) =>
        new(options);

    [Fact(Skip = "TenantsRepository reads are Dapper-only (Postgres) — EF InMemory can't run raw SQL; verify via full-stack E2E.")]
    public async Task AddAsync_Then_ListAsync_ReturnsRegisteredTenants()
    {
        var options = NewOptions();
        await using (var db = NewDb(options))
        {
            var repo = new TenantsRepository(db);
            await repo.AddAsync(new TenantRecord("acme", "Acme Corp", DateTimeOffset.UtcNow));
            await repo.AddAsync(new TenantRecord("globex", "Globex Inc", DateTimeOffset.UtcNow));
        }

        await using (var db = NewDb(options))
        {
            var list = await new TenantsRepository(db).ListAsync();
            list.Count.ShouldBe(2);
            list.Select(t => t.Id).ToList().ShouldBe(ExpectedIds);
        }
    }

    [Fact(Skip = "TenantsRepository reads are Dapper-only (Postgres) — EF InMemory can't run raw SQL; verify via full-stack E2E.")]
    public async Task GetAsync_UnknownId_ReturnsNull()
    {
        await using var db = NewDb(NewOptions());
        var got = await new TenantsRepository(db).GetAsync("nope");
        got.ShouldBeNull();
    }
}
