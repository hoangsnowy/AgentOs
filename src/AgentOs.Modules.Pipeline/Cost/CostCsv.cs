// Renders a CostSummary to CSV for the Cost app's "Export CSV" download. Pure (no I/O) so it is trivially
// testable; invariant formatting (InvariantGlobalization is on solution-wide) and RFC-4180 quoting.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AgentOs.Modules.Pipeline.Persistence;

namespace AgentOs.Modules.Pipeline.Cost;

/// <summary>Serialises a <see cref="CostSummary"/> (totals + per-agent/provider/model/day breakdowns) to CSV.</summary>
public static class CostCsv
{
    /// <summary>The CSV header row.</summary>
    public const string Header = "section,key,cost_usd,tokens_in,tokens_out,calls";

    private static readonly char[] SpecialChars = [',', '"', '\n', '\r'];

    /// <summary>Builds the full CSV: a header, a totals row, then one row per breakdown bucket.</summary>
    public static string ToCsv(CostSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var sb = new StringBuilder();
        sb.Append(Header).Append('\n');
        AppendRow(sb, "total", string.Empty, summary.TotalCostUsd, summary.TotalTokensIn, summary.TotalTokensOut, summary.CallCount);
        AppendBuckets(sb, "agent", summary.ByAgent);
        AppendBuckets(sb, "provider", summary.ByProvider);
        AppendBuckets(sb, "model", summary.ByModel);
        AppendBuckets(sb, "day", summary.ByDay);
        return sb.ToString();
    }

    private static void AppendBuckets(StringBuilder sb, string section, IReadOnlyList<CostBucket> buckets)
    {
        foreach (var bucket in buckets)
        {
            AppendRow(sb, section, bucket.Key, bucket.CostUsd, bucket.TokensIn, bucket.TokensOut, bucket.Calls);
        }
    }

    private static void AppendRow(StringBuilder sb, string section, string key, decimal cost, int tokensIn, int tokensOut, int calls)
    {
        sb.Append(section).Append(',')
          .Append(Escape(key)).Append(',')
          .Append(cost.ToString("0.######", CultureInfo.InvariantCulture)).Append(',')
          .Append(tokensIn.ToString(CultureInfo.InvariantCulture)).Append(',')
          .Append(tokensOut.ToString(CultureInfo.InvariantCulture)).Append(',')
          .Append(calls.ToString(CultureInfo.InvariantCulture)).Append('\n');
    }

    private static string Escape(string value)
    {
        if (value.Length == 0 || value.IndexOfAny(SpecialChars) < 0)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
