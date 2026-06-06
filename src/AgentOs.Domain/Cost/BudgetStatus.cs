// Per-tenant LLM budget state — the result of evaluating month-to-date spend against the configured cap.
// Provider-neutral, lives in Domain. Computed by an IBudgetGuard implementation in a module.

namespace AgentOs.Domain.Cost;

/// <summary>Where this tenant's month-to-date spend sits relative to its cap.</summary>
public enum BudgetState
{
    /// <summary>Under the warn threshold (or no cap configured).</summary>
    Ok,

    /// <summary>At or above the warn threshold (default 80%) but not yet over the cap.</summary>
    Warn,

    /// <summary>Spend has reached or passed the cap.</summary>
    Exceeded,
}

/// <summary>A tenant's budget snapshot: the cap, month-to-date spend, and the derived state.</summary>
public sealed record BudgetStatus(
    decimal CapUsd,
    decimal SpentUsd,
    decimal RemainingUsd,
    double Percent,
    BudgetState State,
    bool EnforceOn)
{
    /// <summary>No cap configured — spend is unconstrained. The standalone / no-database default,
    /// so a missing cap can never block a run.</summary>
    public static BudgetStatus Unset { get; } = new(0m, 0m, 0m, 0d, BudgetState.Ok, false);
}
