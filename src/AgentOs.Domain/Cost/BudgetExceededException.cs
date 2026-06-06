// Thrown by the budget gate when a tenant is over its cap AND enforcement is on. Caught upstream and
// surfaced to the user as a clean "you're over budget" error rather than a generic failure.

namespace AgentOs.Domain.Cost;

/// <summary>Raised when a run is blocked because the tenant's month-to-date LLM spend has reached its
/// enforced budget cap.</summary>
[System.Serializable]
public class BudgetExceededException : System.Exception
{
    /// <summary>The tenant whose budget was exceeded.</summary>
    public string? TenantId { get; }

    /// <summary>The configured monthly cap (USD).</summary>
    public decimal CapUsd { get; }

    /// <summary>Month-to-date spend (USD) at the time of the block.</summary>
    public decimal SpentUsd { get; }

    /// <inheritdoc />
    public BudgetExceededException()
    {
    }

    /// <inheritdoc />
    public BudgetExceededException(string message) : base(message)
    {
    }

    /// <inheritdoc />
    public BudgetExceededException(string message, System.Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>Initializes with the tenant + cap/spend figures, building a user-facing message.</summary>
    public BudgetExceededException(string tenantId, decimal capUsd, decimal spentUsd)
        : base($"Monthly LLM budget reached: spent ${spentUsd:0.00} of the ${capUsd:0.00} cap. "
            + "Raise the cap or turn off enforcement in the Cost app to continue.")
    {
        TenantId = tenantId;
        CapUsd = capUsd;
        SpentUsd = spentUsd;
    }
}
