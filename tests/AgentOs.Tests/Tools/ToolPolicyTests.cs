// The per-tenant tool allowlist policy + its tenant-explicit read/write service. Default-permissive
// until an admin enables enforcement; then only allowlisted tools pass the gate.

using System.Threading.Tasks;
using AgentOs.Domain.Tools;
using AgentOs.Modules.AppConfig;
using AgentOs.Modules.Tools.Policy;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Tools;

public sealed class ToolPolicyTests
{
    private static ToolInvocationRequest Req(string tool, string tenant = "t1") =>
        new(ToolName: tool, CallId: "c1", Input: "{}", TenantId: tenant);

    private static async Task<InMemoryAppConfigStore> Store(bool enforce, string allowlist)
    {
        var cfg = new InMemoryAppConfigStore();
        await cfg.SetForTenantAsync("t1", AppConfigToolPolicy.EnforceKey, enforce ? "true" : "false");
        await cfg.SetForTenantAsync("t1", AppConfigToolPolicy.AllowlistKey, allowlist);
        return cfg;
    }

    [Fact]
    public async Task Evaluate_NotEnforced_AllowsAnyTool()
    {
        var policy = new AppConfigToolPolicy(new InMemoryAppConfigStore());
        (await policy.EvaluateAsync(Req("anything"))).Allowed.ShouldBeTrue();
    }

    [Fact]
    public async Task Evaluate_NoConfigStore_Allows()
    {
        var policy = new AppConfigToolPolicy(config: null);
        (await policy.EvaluateAsync(Req("anything"))).Allowed.ShouldBeTrue();
    }

    [Fact]
    public async Task Evaluate_Enforced_ListedTool_Allows()
    {
        var policy = new AppConfigToolPolicy(await Store(enforce: true, "build_verifier, runner_shell"));
        (await policy.EvaluateAsync(Req("runner_shell"))).Allowed.ShouldBeTrue();
    }

    [Fact]
    public async Task Evaluate_Enforced_UnlistedTool_Denies()
    {
        var policy = new AppConfigToolPolicy(await Store(enforce: true, "build_verifier"));

        var decision = await policy.EvaluateAsync(Req("runner_shell"));

        decision.Allowed.ShouldBeFalse();
        decision.Reason!.ShouldContain("allowlist");
    }

    [Fact]
    public async Task Service_SetAsync_WritesEnforceAndDedupedCsv()
    {
        var cfg = new InMemoryAppConfigStore();
        var service = new ToolPolicyService(cfg);

        await service.SetAsync("t1", enforce: true, ["build_verifier", "runner_shell", "build_verifier"]);

        (await cfg.GetForTenantAsync("t1", AppConfigToolPolicy.EnforceKey)).ShouldBe("true");
        (await cfg.GetForTenantAsync("t1", AppConfigToolPolicy.AllowlistKey)).ShouldBe("build_verifier,runner_shell");
    }

    [Fact]
    public async Task Service_GetAsync_ReadsState()
    {
        var service = new ToolPolicyService(await Store(enforce: true, "x, y"));

        var state = await service.GetAsync("t1");

        state.Enforce.ShouldBeTrue();
        state.AllowedTools.ShouldBe(["x", "y"]);
    }
}
