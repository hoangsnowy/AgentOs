// Test OrchestrationRepository (CRUD) với EF Core InMemory.
using AgenticSdlc.Application.Persistence;
using AgenticSdlc.Infrastructure.Persistence;
using AgenticSdlc.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace AgenticSdlc.Tests.Persistence;

public sealed class OrchestrationRepositoryTests
{
    private static DbContextOptions<AgenticSdlcDbContext> NewOptions() =>
        new DbContextOptionsBuilder<AgenticSdlcDbContext>()
            .UseInMemoryDatabase($"orch-{Guid.NewGuid()}")
            .Options;

    [Fact]
    public async Task UpsertAsync_Insert_ThenGet_ReturnsRecord()
    {
        var options = NewOptions();
        var record = new OrchestrationRecord("g1", "Pipeline", "desc", "{\"nodes\":[]}", DateTimeOffset.UtcNow);

        await using (var db = new AgenticSdlcDbContext(options))
        {
            await new OrchestrationRepository(db).UpsertAsync(record);
        }

        await using (var db = new AgenticSdlcDbContext(options))
        {
            var got = await new OrchestrationRepository(db).GetAsync("g1");
            got.ShouldNotBeNull();
            got.Name.ShouldBe("Pipeline");
            got.DefinitionJson.ShouldBe("{\"nodes\":[]}");
        }
    }

    [Fact]
    public async Task UpsertAsync_ExistingId_UpdatesInPlace()
    {
        var options = NewOptions();
        await using (var db = new AgenticSdlcDbContext(options))
        {
            var repo = new OrchestrationRepository(db);
            await repo.UpsertAsync(new OrchestrationRecord("g1", "Old", null, "{}", DateTimeOffset.UtcNow));
            await repo.UpsertAsync(new OrchestrationRecord("g1", "New", "updated", "{\"x\":1}", DateTimeOffset.UtcNow));
        }

        await using (var db = new AgenticSdlcDbContext(options))
        {
            var all = await new OrchestrationRepository(db).ListAsync();
            all.Count.ShouldBe(1);
            all[0].Name.ShouldBe("New");
            all[0].Description.ShouldBe("updated");
        }
    }

    [Fact]
    public async Task DeleteAsync_RemovesRecord()
    {
        var options = NewOptions();
        await using (var db = new AgenticSdlcDbContext(options))
        {
            await new OrchestrationRepository(db).UpsertAsync(
                new OrchestrationRecord("g1", "Pipeline", null, "{}", DateTimeOffset.UtcNow));
        }

        await using (var db = new AgenticSdlcDbContext(options))
        {
            await new OrchestrationRepository(db).DeleteAsync("g1");
        }

        await using (var db = new AgenticSdlcDbContext(options))
        {
            var got = await new OrchestrationRepository(db).GetAsync("g1");
            got.ShouldBeNull();
        }
    }
}
