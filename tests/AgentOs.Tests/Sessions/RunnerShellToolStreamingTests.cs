// Per-command streaming: when the work runs under a session (AmbientIdentity carries a SessionId),
// runner_shell publishes a Step event per shell command to the live feed. When there is no session,
// it publishes nothing. The dispatch itself is mocked — these tests only assert the feed wiring.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Sessions;
using AgentOs.Domain.Tools;
using AgentOs.Modules.RemoteAgent;
using AgentOs.SharedKernel.Identity;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Sessions;

public sealed class RunnerShellToolStreamingTests
{
    private sealed class CapturingFeed : ISessionRunFeed
    {
        public List<SessionRunEvent> Events { get; } = [];
        public void Publish(SessionRunEvent evt) => Events.Add(evt);
        public IAsyncEnumerable<SessionRunEvent> Subscribe(string tenantId, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private static (RunnerShellTool tool, CapturingFeed feed) MakeTool(bool runnerOk = true)
    {
        var broker = Substitute.For<IRemoteAgentBroker>();
        broker.HasRunnerFor(Arg.Any<RunnerTarget>()).Returns(true);
        broker.DispatchToolCallAsync(Arg.Any<RunnerToolCall>(), Arg.Any<RunnerTarget>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var call = ci.Arg<RunnerToolCall>();
                return runnerOk
                    ? new RunnerToolResult(call.RequestId, call.ToolCallId, true, """{"stdout":"ok"}""", null)
                    : new RunnerToolResult(call.RequestId, call.ToolCallId, false, "", "boom");
            });
        var feed = new CapturingFeed();
        return (new RunnerShellTool(broker, Substitute.For<IHttpContextAccessor>(), feed), feed);
    }

    private static ToolInvocationRequest ShellRequest(string command) => new(
        ToolName: "runner_shell",
        CallId: Guid.NewGuid().ToString("N"),
        Input: $$"""{"command":"{{command}}"}""",
        TenantId: "tenant-1");

    [Fact]
    public async Task InvokeAsync_UnderSession_PublishesPerCommandSteps()
    {
        var (tool, feed) = MakeTool();
        var sessionId = Guid.NewGuid();

        using (AmbientIdentity.Push("tenant-1", "user-1", sessionId))
        {
            await tool.InvokeAsync(ShellRequest("dotnet build"));
        }

        feed.Events.ShouldNotBeEmpty();
        feed.Events.ShouldAllBe(e => e.SessionId == sessionId && e.TenantId == "tenant-1");
        feed.Events.ShouldAllBe(e => e.Kind == SessionRunEventKind.Step);
        feed.Events.ShouldContain(e => e.Message.Contains("dotnet build", StringComparison.Ordinal));
        feed.Events.ShouldContain(e => e.Message.Contains("ok", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvokeAsync_FailedCommand_PublishesFailureStep()
    {
        var (tool, feed) = MakeTool(runnerOk: false);

        using (AmbientIdentity.Push("tenant-1", "user-1", Guid.NewGuid()))
        {
            await tool.InvokeAsync(ShellRequest("dotnet test"));
        }

        feed.Events.ShouldContain(e => e.Message.Contains("failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvokeAsync_NoSession_PublishesNothing()
    {
        var (tool, feed) = MakeTool();

        // No AmbientIdentity session in scope — nothing to tag events to.
        await tool.InvokeAsync(ShellRequest("dotnet build"));

        feed.Events.ShouldBeEmpty();
    }
}
