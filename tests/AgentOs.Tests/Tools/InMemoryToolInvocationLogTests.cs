// Epic E5 — Ring-buffer evidence sink: per-tenant cap, recency order, isolation between tenants.

using System;
using System.Linq;
using System.Threading.Tasks;
using AgentOs.Domain.Tools;
using AgentOs.Modules.Tools.Evidence;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Tools;

public sealed class InMemoryToolInvocationLogTests
{
    [Fact]
    public async Task AppendAndList_ReturnsEntriesNewestFirst()
    {
        var log = new InMemoryToolInvocationLog();
        await log.AppendAsync(Entry("call-1", "tenant-1", "first"));
        await log.AppendAsync(Entry("call-2", "tenant-1", "second"));

        var list = await log.ListRecentAsync("tenant-1");
        list.Count.ShouldBe(2);
        list[0].CallId.ShouldBe("call-2");
        list[1].CallId.ShouldBe("call-1");
    }

    [Fact]
    public async Task List_OtherTenant_ReturnsEmpty()
    {
        var log = new InMemoryToolInvocationLog();
        await log.AppendAsync(Entry("call-1", "tenant-A", "x"));

        (await log.ListRecentAsync("tenant-B")).ShouldBeEmpty();
    }

    [Fact]
    public async Task List_RespectsLimit()
    {
        var log = new InMemoryToolInvocationLog();
        for (var i = 0; i < 10; i++)
        {
            await log.AppendAsync(Entry($"call-{i}", "tenant-1", $"value-{i}"));
        }

        (await log.ListRecentAsync("tenant-1", limit: 3)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task Append_PastPerTenantCap_DropsOldest()
    {
        var log = new InMemoryToolInvocationLog();
        for (var i = 0; i < 600; i++)
        {
            await log.AppendAsync(Entry($"call-{i}", "tenant-1", "x"));
        }

        var list = await log.ListRecentAsync("tenant-1", limit: 1000);
        list.Count.ShouldBe(500);                              // PerTenantCap
        list[0].CallId.ShouldBe("call-599");                   // newest preserved
        list[^1].CallId.ShouldBe("call-100");                  // oldest survivor
    }

    [Fact]
    public async Task List_BlankTenantId_ReturnsEmpty()
    {
        var log = new InMemoryToolInvocationLog();
        (await log.ListRecentAsync(string.Empty)).ShouldBeEmpty();
    }

    private static ToolInvocationEvidence Entry(string callId, string tenantId, string output)
    {
        var now = DateTimeOffset.UtcNow;
        return new ToolInvocationEvidence(
            CallId: callId,
            ToolName: "echo",
            TenantId: tenantId,
            RunId: null,
            Input: "{}",
            Output: output,
            IsError: false,
            StartedUtc: now,
            FinishedUtc: now.AddMilliseconds(5));
    }
}
