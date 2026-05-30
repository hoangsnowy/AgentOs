// Tests OrchestrationRepository (CRUD) with EF Core InMemory.
using AgentOs.Modules.Pipeline.Persistence;
using AgentOs.Modules.Pipeline.Persistence.Repositories;
using AgentOs.Tests.Identity;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Persistence;

public sealed class OrchestrationRepositoryTests
{
    private static DbContextOptions<PipelineDbContext> NewOptions() =>
        new DbContextOptionsBuilder<PipelineDbContext>()
            .UseInMemoryDatabase($"orch-{Guid.NewGuid()}")
            .Options;

    private static readonly TestTenantContext Tenant = new();

    private static PipelineDbContext NewDb(DbContextOptions<PipelineDbContext> options) =>
        new(options, Tenant);

    [Fact]
    public async Task UpsertAsync_Insert_ThenGet_ReturnsRecord()
    {
        var options = NewOptions();
        var record = new OrchestrationRecord("g1", "Pipeline", "desc", "{\"nodes\":[]}", DateTimeOffset.UtcNow);

        await using (var db = NewDb(options))
        {
            await new OrchestrationRepository(db, Tenant).UpsertAsync(record);
        }

        await using (var db = NewDb(options))
        {
            var got = await new OrchestrationRepository(db, Tenant).GetAsync("g1");
            got.ShouldNotBeNull();
            got.Name.ShouldBe("Pipeline");
            got.DefinitionJson.ShouldBe("{\"nodes\":[]}");
        }
    }

    [Fact]
    public async Task UpsertAsync_ExistingId_UpdatesInPlace()
    {
        var options = NewOptions();
        await using (var db = NewDb(options))
        {
            var repo = new OrchestrationRepository(db, Tenant);
            await repo.UpsertAsync(new OrchestrationRecord("g1", "Old", null, "{}", DateTimeOffset.UtcNow));
            await repo.UpsertAsync(new OrchestrationRecord("g1", "New", "updated", "{\"x\":1}", DateTimeOffset.UtcNow));
        }

        await using (var db = NewDb(options))
        {
            var all = await new OrchestrationRepository(db, Tenant).ListAsync();
            all.Count.ShouldBe(1);
            all[0].Name.ShouldBe("New");
            all[0].Description.ShouldBe("updated");
        }
    }

    [Fact]
    public async Task DeleteAsync_RemovesRecord()
    {
        var options = NewOptions();
        await using (var db = NewDb(options))
        {
            await new OrchestrationRepository(db, Tenant).UpsertAsync(
                new OrchestrationRecord("g1", "Pipeline", null, "{}", DateTimeOffset.UtcNow));
        }

        await using (var db = NewDb(options))
        {
            await new OrchestrationRepository(db, Tenant).DeleteAsync("g1");
        }

        await using (var db = NewDb(options))
        {
            var got = await new OrchestrationRepository(db, Tenant).GetAsync("g1");
            got.ShouldBeNull();
        }
    }
}
