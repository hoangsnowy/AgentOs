// Locks the single budget block condition (BudgetStatus.IsBlocking) the API gate, the pipeline
// orchestrator and the workflow executor all share — so the rule can't drift between entry points.

using AgentOs.Domain.Cost;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Cost;

public sealed class BudgetStatusTests
{
    private static BudgetStatus With(BudgetState state, bool enforce)
        => new(CapUsd: 10m, SpentUsd: 12m, RemainingUsd: -2m, Percent: 1.2, state, enforce);

    [Fact]
    public void IsBlocking_OnlyWhenExceeded_AndEnforced()
    {
        With(BudgetState.Exceeded, enforce: true).IsBlocking.ShouldBeTrue();
    }

    [Theory]
    [InlineData(BudgetState.Exceeded, false)] // over cap but enforcement off → warn-only, never block
    [InlineData(BudgetState.Warn, true)]      // under cap → never block
    [InlineData(BudgetState.Ok, true)]
    [InlineData(BudgetState.Warn, false)]
    public void IsBlocking_False_ForEveryOtherCombination(BudgetState state, bool enforce)
    {
        With(state, enforce).IsBlocking.ShouldBeFalse();
    }

    [Fact]
    public void Unset_NeverBlocks()
    {
        BudgetStatus.Unset.IsBlocking.ShouldBeFalse();
    }
}
