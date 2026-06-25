// M1 — DefaultToolGateway: the shared policy-gate + invoke + evidence seam. Denied calls
// short-circuit (the tool never runs) with the reason surfaced + recorded; allowed calls run the
// tool and record the output; tool errors are surfaced + recorded; a null policy/log degrades
// gracefully; an evidence-log failure never breaks the call. These are the invariants the
// remote-session executor (M4) relies on when it routes off-box side effects through the gateway.

using System;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Tools;
using AgentOs.Modules.Tools.Evidence;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Tools;

public sealed class ToolGatewayTests
{
    [Fact]
    public async Task PolicyDenies_ShortCircuits_ToolNotInvoked_EvidenceRecorded()
    {
        var tool = FakeTool();
        var policy = Substitute.For<IToolPolicy>();
        policy.EvaluateAsync(Arg.Any<ToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolPolicyDecision.Deny("blocked for tenant-x"));
        var log = new InMemoryToolInvocationLog();
        var gateway = new DefaultToolGateway(policy, log);

        var result = await gateway.InvokeAsync(tool, Request("tenant-x"), CancellationToken.None);

        result.Denied.ShouldBeTrue();
        result.IsError.ShouldBeTrue();
        result.Output.ShouldBe("blocked for tenant-x");
        await tool.DidNotReceiveWithAnyArgs().InvokeAsync(default!, default);

        var evidence = await log.ListRecentAsync("tenant-x");
        evidence.Count.ShouldBe(1);
        evidence[0].IsError.ShouldBeTrue();
        evidence[0].Output.ShouldBe("blocked for tenant-x");
        evidence[0].ToolName.ShouldBe("echo");
    }

    [Fact]
    public async Task PolicyAllows_ToolSucceeds_RecordsOutput()
    {
        var tool = Substitute.For<ITool>();
        tool.Definition.Returns(new ToolDefinition("echo", "desc", """{"type":"object"}"""));
        tool.InvokeAsync(Arg.Any<ToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(ToolInvocationResult.Success(ci.Arg<ToolInvocationRequest>().CallId, "happy")));
        var log = new InMemoryToolInvocationLog();
        var gateway = new DefaultToolGateway(new AlwaysAllow(), log);

        var result = await gateway.InvokeAsync(tool, Request("tenant-y"), CancellationToken.None);

        result.IsError.ShouldBeFalse();
        result.Denied.ShouldBeFalse();
        result.Output.ShouldBe("happy");
        var evidence = await log.ListRecentAsync("tenant-y");
        evidence.Count.ShouldBe(1);
        evidence[0].IsError.ShouldBeFalse();
        evidence[0].Output.ShouldBe("happy");
    }

    [Fact]
    public async Task ToolReturnsError_SurfacedAndRecorded()
    {
        var tool = Substitute.For<ITool>();
        tool.Definition.Returns(new ToolDefinition("echo", "desc", """{"type":"object"}"""));
        tool.InvokeAsync(Arg.Any<ToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(ToolInvocationResult.Error(ci.Arg<ToolInvocationRequest>().CallId, "boom")));
        var log = new InMemoryToolInvocationLog();
        var gateway = new DefaultToolGateway(new AlwaysAllow(), log);

        var result = await gateway.InvokeAsync(tool, Request("tenant-1"), CancellationToken.None);

        result.IsError.ShouldBeTrue();
        result.Denied.ShouldBeFalse();
        result.Output.ShouldBe("boom");
        (await log.ListRecentAsync("tenant-1"))[0].IsError.ShouldBeTrue();
    }

    [Fact]
    public async Task NoPolicyOrLog_StillInvokes()
    {
        var tool = Substitute.For<ITool>();
        tool.Definition.Returns(new ToolDefinition("echo", "desc", """{"type":"object"}"""));
        tool.InvokeAsync(Arg.Any<ToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(ToolInvocationResult.Success(ci.Arg<ToolInvocationRequest>().CallId, "plain")));
        var gateway = new DefaultToolGateway();

        var result = await gateway.InvokeAsync(tool, Request("tenant-1"), CancellationToken.None);

        result.Output.ShouldBe("plain");
        result.IsError.ShouldBeFalse();
    }

    [Fact]
    public async Task LogThrows_DoesNotBreakInvocation()
    {
        var tool = Substitute.For<ITool>();
        tool.Definition.Returns(new ToolDefinition("echo", "desc", """{"type":"object"}"""));
        tool.InvokeAsync(Arg.Any<ToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(ToolInvocationResult.Success(ci.Arg<ToolInvocationRequest>().CallId, "ok")));
        var log = Substitute.For<IToolInvocationLog>();
        log.AppendAsync(Arg.Any<ToolInvocationEvidence>(), Arg.Any<CancellationToken>())
            .ThrowsAsyncForAnyArgs(new InvalidOperationException("db down"));
        var gateway = new DefaultToolGateway(new AlwaysAllow(), log);

        var result = await gateway.InvokeAsync(tool, Request("tenant-1"), CancellationToken.None);

        result.Output.ShouldBe("ok");
    }

    [Fact]
    public async Task ToolThrows_RecordedAsErrorEvidence_AndSurfacedNotPropagated()
    {
        var tool = Substitute.For<ITool>();
        tool.Definition.Returns(new ToolDefinition("echo", "desc", """{"type":"object"}"""));
        tool.InvokeAsync(Arg.Any<ToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsyncForAnyArgs(new InvalidOperationException("kaboom"));
        var log = new InMemoryToolInvocationLog();
        var gateway = new DefaultToolGateway(new AlwaysAllow(), log);

        // A tool that THROWS (vs returning IsError) must not escape into the agent's chat loop — it becomes
        // a tool_result error, AND the audit trail still records exactly one error row for the failed call.
        var result = await gateway.InvokeAsync(tool, Request("tenant-1"), CancellationToken.None);

        result.IsError.ShouldBeTrue();
        result.Output.ShouldContain("kaboom");
        var evidence = await log.ListRecentAsync("tenant-1");
        evidence.Count.ShouldBe(1);
        evidence[0].IsError.ShouldBeTrue();
    }

    [Fact]
    public async Task ToolThrowsOperationCanceled_Propagates_NotSwallowedAsToolError()
    {
        var tool = Substitute.For<ITool>();
        tool.Definition.Returns(new ToolDefinition("echo", "desc", """{"type":"object"}"""));
        tool.InvokeAsync(Arg.Any<ToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsyncForAnyArgs(new OperationCanceledException());
        var gateway = new DefaultToolGateway(new AlwaysAllow(), new InMemoryToolInvocationLog());

        // Genuine cancellation must propagate, not be recorded as a tool error.
        await Should.ThrowAsync<OperationCanceledException>(
            () => gateway.InvokeAsync(tool, Request("tenant-1"), CancellationToken.None));
    }

    private static ToolInvocationRequest Request(string tenantId)
        => new("echo", Guid.NewGuid().ToString("N"), "{}", tenantId);

    private static ITool FakeTool()
    {
        var tool = Substitute.For<ITool>();
        tool.Definition.Returns(new ToolDefinition("echo", "desc", """{"type":"object"}"""));
        return tool;
    }

    private sealed class AlwaysAllow : IToolPolicy
    {
        public Task<ToolPolicyDecision> EvaluateAsync(ToolInvocationRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(ToolPolicyDecision.Allow);
    }
}
