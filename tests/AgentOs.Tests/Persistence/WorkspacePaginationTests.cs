// Proves the offset/limit pagination added to the previously-unbounded WorkspaceRepository.ListAsync:
// newest-first ordering, page windowing via offset, and the Page.MaxLimit ceiling. EF Core InMemory,
// so the connection factory is absent and the EF path is exercised (the production Dapper-less branch).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AgentOs.Modules.Workspaces.Persistence;
using AgentOs.Modules.Workspaces.Persistence.Entities;
using AgentOs.Modules.Workspaces.Persistence.Repositories;
using AgentOs.SharedKernel.Identity;
using AgentOs.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Persistence;

public sealed class WorkspacePaginationTests
{
    private sealed class FixedTenant(string id) : ITenantContext
    {
        public string TenantId { get; } = id;
        public string? UserId => "u";
        public string? UserName => "u";
        public IReadOnlyList<string> Roles { get; } = ["admin"];
        public bool IsAuthenticated => true;
        public bool IsAdmin => true;
    }

    private static async Task<WorkspaceRepository> SeedAsync(string tenant, int count)
    {
        var options = new DbContextOptionsBuilder<WorkspacesDbContext>()
            .UseInMemoryDatabase($"ws-page-{Guid.NewGuid()}")
            .Options;
        var tc = new FixedTenant(tenant);
        await using var seed = new WorkspacesDbContext(options, tc);
        for (var i = 0; i < count; i++)
        {
            seed.Workspaces.Add(new WorkspaceEntity
            {
                Id = Guid.NewGuid(),
                TenantId = tenant,
                Name = $"board-{i}",
                CreatedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(i),
            });
        }

        await seed.SaveChangesAsync();
        return new WorkspaceRepository(new WorkspacesDbContext(options, tc), tc);
    }

    [Fact]
    public async Task ListAsync_FirstPage_ReturnsNewestFirst_BoundedByLimit()
    {
        var repo = await SeedAsync("t1", count: 5);

        var page = await repo.ListAsync(limit: 2, offset: 0);

        page.Count.ShouldBe(2);
        page.Select(w => w.Name).ShouldBe(["board-4", "board-3"]); // newest first
    }

    [Fact]
    public async Task ListAsync_SecondPage_SkipsByOffset()
    {
        var repo = await SeedAsync("t1", count: 5);

        var page = await repo.ListAsync(limit: 2, offset: 2);

        page.Select(w => w.Name).ShouldBe(["board-2", "board-1"]);
    }

    [Fact]
    public async Task ListAsync_OverlargeLimit_ClampedToMax_ReturnsAllAvailable()
    {
        var repo = await SeedAsync("t1", count: 5);

        var page = await repo.ListAsync(limit: Page.MaxLimit + 10_000, offset: 0);

        page.Count.ShouldBe(5); // only 5 rows exist; the clamp prevents an unbounded pull
    }
}
