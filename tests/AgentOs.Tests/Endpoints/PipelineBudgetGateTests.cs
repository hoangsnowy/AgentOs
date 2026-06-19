// PR #98 follow-up: the direct-agent endpoints (/requirement, /code, /test, /qa) run real billed LLM work
// but — unlike /pipeline — do not go through the gated orchestrator. PipelineBudgetGate is the shared check
// each now calls: it blocks (402) only when the tenant is over an ENFORCED cap, mirroring the orchestrator's
// State == Exceeded && EnforceOn condition, and is a no-op for unset / warn / unenforced budgets.

using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Cost;
using AgentOs.Modules.Pipeline.Endpoints;
using AgentOs.SharedKernel.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Endpoints;

public sealed class PipelineBudgetGateTests
{
    private static ITenantContext Tenant(string id = "acme")
    {
        var t = Substitute.For<ITenantContext>();
        t.TenantId.Returns(id);
        return t;
    }

    private static IBudgetGuard Guard(BudgetStatus status)
    {
        var g = Substitute.For<IBudgetGuard>();
        g.EvaluateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(status);
        return g;
    }

    [Fact]
    public async Task BlockIfExceeded_OverEnforcedCap_Returns402()
    {
        var guard = Guard(new BudgetStatus(10m, 25m, -15m, 2.5, BudgetState.Exceeded, EnforceOn: true));

        var result = await PipelineBudgetGate.BlockIfExceededAsync(guard, Tenant(), CancellationToken.None);

        result.ShouldNotBeNull();
        result.ShouldBeOfType<ProblemHttpResult>().StatusCode.ShouldBe(StatusCodes.Status402PaymentRequired);
    }

    [Fact]
    public async Task BlockIfExceeded_OverCapButEnforcementOff_AllowsRun()
    {
        var guard = Guard(new BudgetStatus(10m, 25m, -15m, 2.5, BudgetState.Exceeded, EnforceOn: false));

        (await PipelineBudgetGate.BlockIfExceededAsync(guard, Tenant(), CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task BlockIfExceeded_WarnState_AllowsRun()
    {
        var guard = Guard(new BudgetStatus(100m, 85m, 15m, 0.85, BudgetState.Warn, EnforceOn: true));

        (await PipelineBudgetGate.BlockIfExceededAsync(guard, Tenant(), CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task BlockIfExceeded_CapUnset_AllowsRun()
    {
        var guard = Guard(BudgetStatus.Unset);

        (await PipelineBudgetGate.BlockIfExceededAsync(guard, Tenant(), CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task BlockIfExceeded_EvaluatesTheCallersTenant()
    {
        var guard = Guard(BudgetStatus.Unset);

        await PipelineBudgetGate.BlockIfExceededAsync(guard, Tenant("tenant-7"), CancellationToken.None);

        await guard.Received(1).EvaluateAsync("tenant-7", Arg.Any<CancellationToken>());
    }
}
