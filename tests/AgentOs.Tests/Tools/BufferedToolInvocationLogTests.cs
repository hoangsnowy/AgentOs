// M6 perf — the buffered evidence log must take writes OFF the caller's path (enqueue + return), drain
// to the inner sink (background loop or explicit drain), delegate reads, and never throw.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Tools;
using AgentOs.Modules.Tools.Evidence;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Tools;

public sealed class BufferedToolInvocationLogTests
{
    // Thread-safe recorder (the background drain loop appends off-thread while the test reads).
    private sealed class FakeInnerLog : IToolInvocationLog
    {
        public ConcurrentQueue<ToolInvocationEvidence> Appended { get; } = new();

        public Task AppendAsync(ToolInvocationEvidence entry, CancellationToken cancellationToken = default)
        {
            Appended.Enqueue(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ToolInvocationEvidence>> ListRecentAsync(
            string tenantId, int limit = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ToolInvocationEvidence>>(Appended.ToArray());
    }

    private static ToolInvocationEvidence Evt(string callId)
    {
        var now = DateTimeOffset.UtcNow;
        return new ToolInvocationEvidence(callId, "runner_shell", "t1", null, "{}", "ok", false, now, now, "sess-1");
    }

    [Fact]
    public async Task AppendAsync_IsNonBlocking_AndDoesNotWriteToInnerSynchronously()
    {
        var inner = new FakeInnerLog();
        await using var buffered = new BufferedToolInvocationLog(inner);

        var task = buffered.AppendAsync(Evt("c1"));
        task.IsCompletedSuccessfully.ShouldBeTrue();   // returns synchronously — off the critical path
        await task;
        await buffered.AppendAsync(Evt("c2"));

        // Nothing started a drain → the inner sink hasn't been touched yet.
        inner.Appended.Count.ShouldBe(0);
    }

    [Fact]
    public async Task DrainPendingAsync_FlushesQueuedEntriesToInner()
    {
        var inner = new FakeInnerLog();
        await using var buffered = new BufferedToolInvocationLog(inner);

        await buffered.AppendAsync(Evt("c1"));
        await buffered.AppendAsync(Evt("c2"));
        await buffered.DrainPendingAsync();

        inner.Appended.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ListRecentAsync_DelegatesToInner()
    {
        var inner = new FakeInnerLog();
        await using var buffered = new BufferedToolInvocationLog(inner);
        await buffered.AppendAsync(Evt("c1"));
        await buffered.DrainPendingAsync();

        var rows = await buffered.ListRecentAsync("t1");
        rows.Count.ShouldBe(1);
        rows[0].CallId.ShouldBe("c1");
    }

    [Fact]
    public async Task StartDraining_PersistsEnqueuedEntries_InBackground()
    {
        var inner = new FakeInnerLog();
        await using var buffered = new BufferedToolInvocationLog(inner);
        buffered.StartDraining();

        await buffered.AppendAsync(Evt("c1"));
        await buffered.AppendAsync(Evt("c2"));

        // The background loop drains asynchronously — poll until both land (bounded).
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (inner.Appended.Count < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        inner.Appended.Count.ShouldBe(2);
    }
}
