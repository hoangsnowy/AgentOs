// AgentOs.Tests/Cost/BudgetServiceTests.cs
// The tenant-explicit budget service the Cost app uses: writes land under the per-tenant AppConfig keys
// (invariant cap string, boolean enforce flag) and reads pass through to the IBudgetGuard.

using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Cost;
using AgentOs.Modules.AppConfig;
using AgentOs.Modules.Pipeline.Cost;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Cost;

public sealed class BudgetServiceTests
{
    [Fact]
    public async Task SetCapAsync_WritesTenantExplicitKey()
    {
        var config = new InMemoryAppConfigStore();
        var service = new BudgetService(config, Substitute.For<IBudgetGuard>());

        await service.SetCapAsync("t1", 100m);

        (await config.GetForTenantAsync("t1", BudgetGuard.CapKey)).ShouldBe("100");
    }

    [Fact]
    public async Task SetCapAsync_ZeroOrLess_ClearsTheKey()
    {
        var config = new InMemoryAppConfigStore();
        await config.SetForTenantAsync("t1", BudgetGuard.CapKey, "50");
        var service = new BudgetService(config, Substitute.For<IBudgetGuard>());

        await service.SetCapAsync("t1", 0m);

        (await config.GetForTenantAsync("t1", BudgetGuard.CapKey)).ShouldBeNull();
    }

    [Fact]
    public async Task SetEnforceAsync_WritesBooleanString()
    {
        var config = new InMemoryAppConfigStore();
        var service = new BudgetService(config, Substitute.For<IBudgetGuard>());

        await service.SetEnforceAsync("t1", true);

        (await config.GetForTenantAsync("t1", BudgetGuard.EnforceKey)).ShouldBe("true");
    }

    [Fact]
    public async Task GetAsync_DelegatesToGuard()
    {
        var expected = new BudgetStatus(100m, 40m, 60m, 0.4, BudgetState.Ok, EnforceOn: false);
        var guard = Substitute.For<IBudgetGuard>();
        guard.EvaluateAsync("t1", Arg.Any<CancellationToken>()).Returns(expected);
        var service = new BudgetService(new InMemoryAppConfigStore(), guard);

        var status = await service.GetAsync("t1");

        status.ShouldBe(expected);
    }
}
