// Tests the in-memory live session-run event stream (M6): per-tenant fan-out, isolation across
// tenants, no-throw when nobody is listening, and clean stop on cancellation. Timing-based where a
// subscription must register before a publish — kept generous + bounded so it is not flaky.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Sessions;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Sessions;

public sealed class SessionRunStreamTests
{
    private static SessionRunEvent Evt(string tenant, Guid session, string msg) =>
        new(tenant, session, SessionRunEventKind.Step, msg, DateTimeOffset.UtcNow);

    [Fact]
    public void Publish_NoSubscriber_DoesNotThrow()
    {
        var stream = new InMemorySessionRunFeed();
        Should.NotThrow(() => stream.Publish(Evt("t1", Guid.NewGuid(), "ping")));
    }

    [Fact]
    public async Task Publish_ReachesSubscriberOfSameTenant()
    {
        var stream = new InMemorySessionRunFeed();
        var session = Guid.NewGuid();
        var got = new List<SessionRunEvent>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var sub = Task.Run(async () =>
        {
            await foreach (var e in stream.Subscribe("t1", cts.Token))
            {
                got.Add(e);
                break;
            }
        });

        await Task.Delay(120);                 // let the subscription register its channel
        stream.Publish(Evt("t1", session, "hello"));

        try { await sub; } catch (OperationCanceledException ex) { _ = ex.Message; } // expected on stream stop

        got.ShouldHaveSingleItem();
        got[0].SessionId.ShouldBe(session);
        got[0].Message.ShouldBe("hello");
    }

    [Fact]
    public async Task Publish_OtherTenant_IsNotReceived()
    {
        var stream = new InMemorySessionRunFeed();
        var mine = Guid.NewGuid();
        var got = new List<SessionRunEvent>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var sub = Task.Run(async () =>
        {
            await foreach (var e in stream.Subscribe("t1", cts.Token))
            {
                got.Add(e);
                break;
            }
        });

        await Task.Delay(120);
        stream.Publish(Evt("t2", Guid.NewGuid(), "other-tenant"));  // must be filtered out
        stream.Publish(Evt("t1", mine, "mine"));                    // the one we should receive

        try { await sub; } catch (OperationCanceledException ex) { _ = ex.Message; } // expected on stream stop

        got.ShouldHaveSingleItem();
        got[0].SessionId.ShouldBe(mine);
        got[0].Message.ShouldBe("mine");
    }

    [Fact]
    public async Task TwoSubscribers_SameTenant_BothReceive()
    {
        var stream = new InMemorySessionRunFeed();
        var session = Guid.NewGuid();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        async Task<SessionRunEvent?> One()
        {
            await foreach (var e in stream.Subscribe("t1", cts.Token))
            {
                return e;
            }
            return null;
        }

        var a = Task.Run(One);
        var b = Task.Run(One);
        await Task.Delay(120);
        stream.Publish(Evt("t1", session, "fanout"));

        var results = await Task.WhenAll(a, b);

        results[0]?.SessionId.ShouldBe(session);
        results[1]?.SessionId.ShouldBe(session);
    }

    [Fact]
    public async Task Subscribe_Cancellation_StopsEnumeration()
    {
        var stream = new InMemorySessionRunFeed();
        using var cts = new CancellationTokenSource();

        var sub = Task.Run(async () =>
        {
            await foreach (var e in stream.Subscribe("t1", cts.Token))
            {
                _ = e;
            }
        });

        await Task.Delay(80);
        cts.Cancel();

        var finished = await Task.WhenAny(sub, Task.Delay(TimeSpan.FromSeconds(2)));
        finished.ShouldBe(sub, "cancellation should end the subscription enumeration");
        try { await sub; } catch (OperationCanceledException ex) { _ = ex.Message; } // expected on stream stop
    }
}
