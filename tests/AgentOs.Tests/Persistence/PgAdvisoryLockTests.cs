// Batch 3 — migration coordination: the advisory lock no-ops without a real connection string
// (tests / stateless boot) and hashes lock names deterministically (key must be identical across
// replicas and restarts — string.GetHashCode is per-process randomized and would never collide).
using AgentOs.SharedKernel.Persistence;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Persistence;

public class PgAdvisoryLockTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AcquireAsync_NoConnectionString_ReturnsNoOpHandle(string? connectionString)
    {
        await using var handle = await PgAdvisoryLock.AcquireAsync(connectionString, "agentos:migrate:test");
        handle.ShouldNotBeNull(); // dispose of the no-op handle must not throw
    }

    [Fact]
    public void Fnv1aHash_SameName_SameKey() =>
        PgAdvisoryLock.Fnv1aHash("agentos:migrate:pipeline")
            .ShouldBe(PgAdvisoryLock.Fnv1aHash("agentos:migrate:pipeline"));

    [Fact]
    public void Fnv1aHash_DifferentNames_DifferentKeys() =>
        PgAdvisoryLock.Fnv1aHash("agentos:migrate:pipeline")
            .ShouldNotBe(PgAdvisoryLock.Fnv1aHash("agentos:migrate:tenants"));

    [Fact]
    public async Task AcquireAsync_EmptyLockName_Throws() =>
        await Should.ThrowAsync<ArgumentException>(
            async () => await PgAdvisoryLock.AcquireAsync(null, " "));
}
