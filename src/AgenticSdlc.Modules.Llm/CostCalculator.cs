// Compute cost by model + tokens. Hardcoded pricing (Q2/2026 snapshot).

using System.Collections.Generic;

namespace AgenticSdlc.Modules.Llm;

/// <summary>
/// Static cost calculator. Pricing (USD per 1M tokens):
/// claude-sonnet-4 (3/15), claude-haiku-4-5 (1/5), gpt-4.1 (2.5/10), gpt-4o-mini (0.15/0.60).
/// Match is case-insensitive StartsWith — allows suffixes like "claude-sonnet-4-20250514".
/// </summary>
public static class CostCalculator
{
    private static readonly IReadOnlyList<ModelPricing> Pricings = new ModelPricing[]
    {
        new("claude-sonnet-4",  InputPerMillion: 3.00m,  OutputPerMillion: 15.00m),
        new("claude-haiku-4-5", InputPerMillion: 1.00m,  OutputPerMillion: 5.00m),
        new("gpt-4.1",          InputPerMillion: 2.50m,  OutputPerMillion: 10.00m),
        new("gpt-4o-mini",      InputPerMillion: 0.15m,  OutputPerMillion: 0.60m),
    };

    /// <summary>Cost (USD) for one (inputTokens, outputTokens) pair. Unknown model → 0m.</summary>
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
                return System.Math.Round(input + output, 6, System.MidpointRounding.AwayFromZero);
            }
        }

        return 0m;
    }

    /// <summary>True if pricing for the model exists in the table.</summary>
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
