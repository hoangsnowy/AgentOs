// M1 — evidence: the gateway threads SessionId into the evidence, and the durable EF-backed log
// persists + lists per tenant (verified against the EF in-memory provider).

using System;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Tools;
using AgentOs.Modules.Tools.Evidence;
using AgentOs.Modules.Tools.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Tools;

public sealed class EvidenceTests
{
    [Fact]
    public async Task Gateway_RecordsSessionId_OnTheEvidence()
    {
        var tool = Substitute.For<ITool>();
        tool.InvokeAsync(Arg.Any<ToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolInvocationResult.Success("c1", "done"));
        var log = Substitute.For<IToolInvocationLog>();
        var gateway = new DefaultToolGateway(policy: null, log: log);

        var request = new ToolInvocationRequest("build_verifier", "c1", "{}", "tenant-1", RunId: "run-7", SessionId: "sess-9");
        await gateway.InvokeAsync(tool, request);

        await log.Received(1).AppendAsync(
            Arg.Is<ToolInvocationEvidence>(e => e.SessionId == "sess-9" && e.RunId == "run-7"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EfLog_Append_PersistsAndListsPerTenant_WithSessionId()
    {
        await using var sp = BuildProvider();
        var log = new EfToolInvocationLog(sp, sp.GetRequiredService<ILogger<EfToolInvocationLog>>());

        var now = DateTimeOffset.UtcNow;
        await log.AppendAsync(new ToolInvocationEvidence("c1", "build", "t1", "run-1", "{}", "ok", false, now, now.AddSeconds(1), "sess-1"));
        await log.AppendAsync(new ToolInvocationEvidence("c2", "test", "t1", null, "{}", "fail", true, now.AddSeconds(2), now.AddSeconds(3)));
        await log.AppendAsync(new ToolInvocationEvidence("c3", "other", "t2", null, "{}", "ok", false, now, now.AddSeconds(1)));

        var t1 = await log.ListRecentAsync("t1");
        t1.Count.ShouldBe(2);
        t1[0].ToolName.ShouldBe("test");           // newest first (by StartedUtc)
        t1[1].ToolName.ShouldBe("build");
        t1[1].SessionId.ShouldBe("sess-1");        // SessionId round-trips
        t1[0].IsError.ShouldBeTrue();

        var t2 = await log.ListRecentAsync("t2");   // tenant isolation
        t2.Count.ShouldBe(1);
        t2[0].ToolName.ShouldBe("other");
    }

    [Fact]
    public async Task EfLog_ListRecent_RespectsLimit()
    {
        await using var sp = BuildProvider();
        var log = new EfToolInvocationLog(sp, sp.GetRequiredService<ILogger<EfToolInvocationLog>>());
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 5; i++)
        {
            await log.AppendAsync(new ToolInvocationEvidence($"c{i}", "t", "t1", null, "{}", "ok", false, now.AddSeconds(i), now.AddSeconds(i + 1)));
        }

        (await log.ListRecentAsync("t1", limit: 2)).Count.ShouldBe(2);
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // EfToolInvocationLog creates a fresh DI scope per op, so the in-memory store must be shared
        // across context instances — a shared InMemoryDatabaseRoot guarantees that.
        var root = new InMemoryDatabaseRoot();
        services.AddDbContext<ToolsDbContext>(o => o.UseInMemoryDatabase("tools-test", root));
        return services.BuildServiceProvider();
    }
}
