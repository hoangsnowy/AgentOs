// AgentOs.Tests/Llm/RemoteAgentTests.cs
// Unit tests for the "remote dev-IDE agent" runtime: the dispatch broker + the RemoteAgentLlmClient
// that wraps a remote reply as a zero-cost LlmResponse.
//
// M3 — dispatch is targeted (tenant + member), not broadcast. A runner registers with a RunnerConnection
// identity; a RunnerTarget resolves to exactly one connection within its tenant.

using System;
using System.Threading.Tasks;
using AgentOs.Domain.Llm;
using AgentOs.Modules.RemoteAgent;
using AgentOs.SharedKernel.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Llm;

public class RemoteAgentTests
{
    private static RunnerConnection Conn(string tenant = "default", string member = "member-1") =>
        new(Guid.NewGuid(), tenant, member);

    private static RunnerTarget Target(string tenant = "default", string member = "member-1") =>
        new(tenant, member);

    private static ITenantContext Tenant(string tenant = "default", string? member = "member-1")
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(tenant);
        ctx.UserId.Returns(member);
        return ctx;
    }

    [Fact]
    public async Task Broker_DispatchThenComplete_ReturnsRunnerResult()
    {
        var broker = new InProcessRemoteAgentBroker();
        using var reg = broker.RegisterRunner("conn-1", Conn());
        RemoteDispatch? dispatched = null;
        broker.Dispatched += d => dispatched = d;

        var task = broker.DispatchAsync(new RemoteExecRequest("id1", "sys", "user", "model"), Target(), TimeSpan.FromSeconds(5));
        dispatched.ShouldNotBeNull();
        dispatched!.Request.Id.ShouldBe("id1");
        dispatched.ConnectionId.ShouldBe("conn-1");

        broker.Complete(new RemoteExecResult("id1", true, "result text", null));

        var result = await task;
        result.Ok.ShouldBeTrue();
        result.Content.ShouldBe("result text");
    }

    [Fact]
    public async Task Broker_NoRunner_DispatchThrows()
    {
        var broker = new InProcessRemoteAgentBroker();
        broker.HasAgent.ShouldBeFalse();

        await Should.ThrowAsync<InvalidOperationException>(
            () => broker.DispatchAsync(new RemoteExecRequest("x", "s", "u", "m"), Target(), TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task Broker_NoResponse_TimesOut()
    {
        var broker = new InProcessRemoteAgentBroker();
        using var reg = broker.RegisterRunner("conn-1", Conn());

        await Should.ThrowAsync<TimeoutException>(
            () => broker.DispatchAsync(new RemoteExecRequest("id", "s", "u", "m"), Target(), TimeSpan.FromMilliseconds(50)));
    }

    [Fact]
    public void Broker_RunnerDisposed_NoLongerConnected()
    {
        var broker = new InProcessRemoteAgentBroker();
        var reg = broker.RegisterRunner("conn-1", Conn());
        broker.HasAgent.ShouldBeTrue();
        reg.Dispose();
        broker.HasAgent.ShouldBeFalse();
    }

    [Fact]
    public void Broker_HasRunnerFor_MatchesTenantAndMember()
    {
        var broker = new InProcessRemoteAgentBroker();
        using var reg = broker.RegisterRunner("conn-1", Conn("tenant-a", "alice"));

        broker.HasRunnerFor(Target("tenant-a", "alice")).ShouldBeTrue();
        broker.HasRunnerFor(Target("tenant-a", "bob")).ShouldBeFalse();
    }

    [Fact]
    public void Broker_DoesNotResolveAcrossTenants()
    {
        var broker = new InProcessRemoteAgentBroker();
        using var reg = broker.RegisterRunner("conn-1", Conn("tenant-a", "alice"));

        // Same member id but a different tenant must NOT resolve — closes the cross-tenant leak.
        broker.HasRunnerFor(Target("tenant-b", "alice")).ShouldBeFalse();
    }

    [Fact]
    public async Task Broker_TargetsTheMatchingMembersConnection()
    {
        var broker = new InProcessRemoteAgentBroker();
        using var a = broker.RegisterRunner("conn-alice", Conn("tenant-a", "alice"));
        using var b = broker.RegisterRunner("conn-bob", Conn("tenant-a", "bob"));
        RemoteDispatch? dispatched = null;
        broker.Dispatched += d => dispatched = d;

        var task = broker.DispatchAsync(new RemoteExecRequest("id", "s", "u", "m"), Target("tenant-a", "bob"), TimeSpan.FromSeconds(5));
        dispatched!.ConnectionId.ShouldBe("conn-bob");

        broker.Complete(new RemoteExecResult("id", true, "ok", null));
        (await task).Ok.ShouldBeTrue();
    }

    [Fact]
    public void Broker_EmptyMember_MatchesAnyRunnerInTenant()
    {
        var broker = new InProcessRemoteAgentBroker();
        using var reg = broker.RegisterRunner("conn-1", Conn("default", "operator-pseudo"));

        // Operator mode: no member id on the target → any runner in the tenant is a match.
        broker.HasRunnerFor(new RunnerTarget("default", string.Empty)).ShouldBeTrue();
    }

    [Fact]
    public async Task Client_WithRunner_ReturnsContent_AtZeroCost()
    {
        var broker = new InProcessRemoteAgentBroker();
        using var reg = broker.RegisterRunner("conn-1", Conn());
        // Auto-answer every dispatched request as the remote runner would.
        broker.Dispatched += d => broker.Complete(new RemoteExecResult(d.Request.Id, true, "// generated by dev IDE", null));

        var client = new RemoteAgentLlmClient(broker, Tenant(), NullLogger<RemoteAgentLlmClient>.Instance);
        var response = await client.SendAsync(new LlmRequest("sys", "build X", "claude-sonnet-4"));

        response.Content.ShouldBe("// generated by dev IDE");
        response.CostUsd.ShouldBe(0m);
        response.InputTokens.ShouldBe(0);
        response.OutputTokens.ShouldBe(0);
        response.Provider.ShouldBe("RemoteAgent");
    }

    [Fact]
    public async Task Client_NoRunner_ThrowsLlmException()
    {
        var broker = new InProcessRemoteAgentBroker();
        var client = new RemoteAgentLlmClient(broker, Tenant(), NullLogger<RemoteAgentLlmClient>.Instance);

        await Should.ThrowAsync<LlmException>(
            () => client.SendAsync(new LlmRequest("sys", "build X", "model")));
    }

    [Fact]
    public async Task Client_RunnerReportsFailure_ThrowsLlmException()
    {
        var broker = new InProcessRemoteAgentBroker();
        using var reg = broker.RegisterRunner("conn-1", Conn());
        broker.Dispatched += d => broker.Complete(new RemoteExecResult(d.Request.Id, false, "", "compile failed"));

        var client = new RemoteAgentLlmClient(broker, Tenant(), NullLogger<RemoteAgentLlmClient>.Instance);

        var ex = await Should.ThrowAsync<LlmException>(
            () => client.SendAsync(new LlmRequest("sys", "build X", "model")));
        ex.Message.ShouldContain("compile failed");
    }
}
