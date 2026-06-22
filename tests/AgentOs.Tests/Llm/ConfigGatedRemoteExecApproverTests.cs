// Unit tests for ConfigGatedRemoteExecApprover — the fail-closed default approver for remote/runner
// execution. Outside Development it must DENY unless RemoteAgent:AutoApprove=true is set explicitly,
// so no deployment ships an unattended runner_shell / remote-dispatch path by accident.

using System.Collections.Generic;
using System.Threading.Tasks;
using AgentOs.Modules.RemoteAgent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Llm;

public class ConfigGatedRemoteExecApproverTests
{
    private static readonly RemoteExecRequest Request = new("req-1", "sys", "user", "model");
    private static readonly RunnerToolCall ToolCall = new("req-1", "tc-1", "runner_shell", "{}");

    private static ConfigGatedRemoteExecApprover Build(string environmentName, bool? autoApprove)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(environmentName);

        var settings = new Dictionary<string, string?>();
        if (autoApprove.HasValue)
        {
            settings[ConfigGatedRemoteExecApprover.AutoApproveKey] = autoApprove.Value ? "true" : "false";
        }
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return new ConfigGatedRemoteExecApprover(env, config, NullLogger<ConfigGatedRemoteExecApprover>.Instance);
    }

    [Fact]
    public async Task ApproveAsync_ProductionNoConfig_Denies()
    {
        var approver = Build("Production", autoApprove: null);

        (await approver.ApproveAsync(Request)).ShouldBeFalse();
        (await approver.ApproveToolCallAsync(ToolCall)).ShouldBeFalse();
    }

    [Fact]
    public async Task ApproveAsync_DevelopmentNoConfig_Approves()
    {
        var approver = Build("Development", autoApprove: null);

        (await approver.ApproveAsync(Request)).ShouldBeTrue();
        (await approver.ApproveToolCallAsync(ToolCall)).ShouldBeTrue();
    }

    [Fact]
    public async Task ApproveAsync_ProductionExplicitOptIn_Approves()
    {
        var approver = Build("Production", autoApprove: true);

        (await approver.ApproveAsync(Request)).ShouldBeTrue();
        (await approver.ApproveToolCallAsync(ToolCall)).ShouldBeTrue();
    }

    [Fact]
    public async Task ApproveAsync_DevelopmentExplicitOptOut_Denies()
    {
        var approver = Build("Development", autoApprove: false);

        (await approver.ApproveAsync(Request)).ShouldBeFalse();
        (await approver.ApproveToolCallAsync(ToolCall)).ShouldBeFalse();
    }
}
