// AgenticSdlc.Infrastructure/Llm/CostCalculator.cs
// Sprint 1 — Compute cost by model + tokens. Hardcoded pricing (Q2/2026 snapshot).

using System.Collections.Generic;

namespace AgenticSdlc.Infrastructure.Llm;

/// <summary>
/// Static cost calculator. Pricing (USD per 1M tokens):
/// <list type="bullet">
///   <item>claude-sonnet-4 — input $3.00, output $15.00</item>
///   <item>claude-haiku-4-5 — input $1.00, output $5.00</item>
///   <item>gpt-4.1 — input $2.50, output $10.00</item>
///   <item>gpt-4o-mini — input $0.15, output $0.60</item>
/// </list>
/// Model name matching is case-insensitive and uses <c>StartsWith</c> (allows suffixes like "claude-sonnet-4-20250514").
/// </summary>
public static class CostCalculator
{
    /// <summary>Pricing table (USD per 1 token).</summary>
    private static readonly IReadOnlyList<ModelPricing> Pricings = new ModelPricing[]
    {
        new("claude-sonnet-4",  InputPerMillion: 3.00m,  OutputPerMillion: 15.00m),
        new("claude-haiku-4-5", InputPerMillion: 1.00m,  OutputPerMillion: 5.00m),
        new("gpt-4.1",          InputPerMillion: 2.50m,  OutputPerMillion: 10.00m),
        new("gpt-4o-mini",      InputPerMillion: 0.15m,  OutputPerMillion: 0.60m),
    };

    /// <summary>
    /// Computes the cost (USD) for a single (inputTokens, outputTokens) pair on a model.
    /// If the model does not match the pricing table, returns <c>0m</c> (so offline tests are not broken; logging a warning to ILogger is the caller's responsibility).
    /// </summary>
    public static decimal Calculate(string model, int inputTokens, int outputTokens)
    {
        if (string.IsNullOrWhiteSpace(model) || inputTokens < 0 || outputTokens < 0)
        {
            return 0m;
        }

        foreach (var p in Pricings)
        {
            if (model.StartsWith(p.ModelPrefix, System.StringComparison.OrdinalIgnoreCase))
            {
                var input = (decimal)inputTokens / 1_000_000m * p.InputPerMillion;
                var output = (decimal)outputTokens / 1_000_000m * p.OutputPerMillion;
                // Round to 6 decimal places — enough resolution for the total pipeline cost.
                return System.Math.Round(input + output, 6, System.MidpointRounding.AwayFromZero);
            }
        }

        return 0m;
    }

    /// <summary>Returns true if pricing for the model exists in the table.</summary>
    public static bool IsKnown(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        foreach (var p in Pricings)
        {
            if (model.StartsWith(p.ModelPrefix, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private sealed record ModelPricing(string ModelPrefix, decimal InputPerMillion, decimal OutputPerMillion);
}
