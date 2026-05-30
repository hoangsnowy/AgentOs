// Epic E5 — AIToolFunction integrates IToolPolicy + IToolInvocationLog: denied calls short-circuit
// with the reason surfaced to the LLM, every call (allowed, errored or denied) writes evidence,
// and evidence-log failures must not break the tool call.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Tools;
using AgentOs.Modules.Llm;
using AgentOs.Modules.Tools.Evidence;
using Microsoft.Extensions.AI;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Llm;

public sealed class AIToolFunctionPolicyTests
{
    [Fact]
    public async Task PolicyDenies_ShortCircuitsAndLogsEvidence()
    {
        var tool = FakeTool();
        var policy = Substitute.For<IToolPolicy>();
        policy.EvaluateAsync(Arg.Any<ToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ToolPolicyDecision.Deny("not allowed for tenant-x"));
        var log = new InMemoryToolInvocationLog();

        var fn = new AIToolFunction(tool, "tenant-x", runId: "run-1", policy: policy, log: log);
        var result = await fn.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>()), CancellationToken.None);

        result?.ToString().ShouldBe("not allowed for tenant-x");
        await tool.DidNotReceiveWithAnyArgs().InvokeAsync(default!, default);

        var evidence = await log.ListRecentAsync("tenant-x");
        evidence.Count.ShouldBe(1);
        evidence[0].IsError.ShouldBeTrue();
        evidence[0].Output.ShouldBe("not allowed for tenant-x");
        evidence[0].ToolName.ShouldBe("echo");
    }

    [Fact]
    public async Task PolicyAllowsAndToolSucceeds_LogsAllowedEvidence()
    {
        var tool = Substitute.For<ITool>();
        tool.Definition.Returns(new ToolDefinition("echo", "desc", """{"type":"object"}"""));
        tool.InvokeAsync(Arg.Any<ToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(ToolInvocationResult.Success(ci.Arg<ToolInvocationRequest>().CallId, "happy")));
        var log = new InMemoryToolInvocationLog();

        var fn = new AIToolFunction(tool, "tenant-y", runId: null, policy: new AlwaysAllow(), log: log);
        var result = await fn.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["arg"] = 1 }), CancellationToken.None);

        result?.ToString().ShouldBe("happy");
        var evidence = await log.ListRecentAsync("tenant-y");
        evidence.Count.ShouldBe(1);
        evidence[0].IsError.ShouldBeFalse();
        evidence[0].Output.ShouldBe("happy");
        evidence[0].Input.ShouldContain("arg");
    }

    [Fact]
    public async Task NoPolicyOrLog_StillInvokesAndReturnsOutput()
    {
        var tool = Substitute.For<ITool>();
        tool.Definition.Returns(new ToolDefinition("echo", "desc", """{"type":"object"}"""));
        tool.InvokeAsync(Arg.Any<ToolInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(ToolInvocationResult.Success(ci.Arg<ToolInvocationRequest>().CallId, "plain")));

        var fn = new AIToolFunction(tool, "tenant-1", runId: null);
        var result = await fn.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>()), CancellationToken.None);

        result?.ToString().ShouldBe("plain");
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

        var fn = new AIToolFunction(tool, "tenant-1", runId: null, policy: null, log: log);
        var result = await fn.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>()), CancellationToken.None);

        result?.ToString().ShouldBe("ok");
    }

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
