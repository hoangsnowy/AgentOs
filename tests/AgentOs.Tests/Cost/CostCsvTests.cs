// AgentOs.Tests/Cost/CostCsvTests.cs
// The cost CSV export: header + zero total for an empty summary, invariant decimal formatting (no locale
// comma), and RFC-4180 quoting of keys that contain a comma.

using System;
using AgentOs.Modules.Pipeline.Cost;
using AgentOs.Modules.Pipeline.Persistence;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Cost;

public sealed class CostCsvTests
{
    private static CostSummary OneModel(string key, decimal cost)
        => new(cost, 10, 5, 1, 1, [], [], [new CostBucket(key, cost, 10, 5, 1)], []);

    [Fact]
    public void ToCsv_EmptySummary_EmitsHeaderAndZeroTotalOnly()
    {
        var csv = CostCsv.ToCsv(CostSummary.Empty);

        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.ShouldBe(2);
        lines[0].ShouldBe(CostCsv.Header);
        lines[1].ShouldStartWith("total,");
        csv.ShouldNotContain("agent,");
        csv.ShouldNotContain("model,");
    }

    [Fact]
    public void ToCsv_WithBreakdowns_RendersInvariantDecimals()
    {
        var csv = CostCsv.ToCsv(OneModel("claude-sonnet-4", 0.04m));

        csv.ShouldContain("model,claude-sonnet-4,0.04,");
        csv.ShouldNotContain("0,04"); // never a locale decimal comma
    }

    [Fact]
    public void ToCsv_EscapesKeysWithCommas()
    {
        var csv = CostCsv.ToCsv(OneModel("gpt-4, turbo", 1m));

        csv.ShouldContain("model,\"gpt-4, turbo\",1,");
    }
}
