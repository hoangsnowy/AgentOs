// AgenticSdlc.Tests/Domain/AgentMetricsTests.cs
// Phase 3 — AgentMetrics.Add + Empty.

using System;
using AgenticSdlc.Domain;
using Shouldly;
using Xunit;

namespace AgenticSdlc.Tests.Domain;

public class AgentMetricsTests
{
    [Fact]
    public void Empty_ZeroValues()
    {
        AgentMetrics.Empty.InputTokens.ShouldBe(0);
        AgentMetrics.Empty.OutputTokens.ShouldBe(0);
        AgentMetrics.Empty.CostUsd.ShouldBe(0m);
        AgentMetrics.Empty.Latency.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Add_TwoMetrics_SumNumericFieldsConcatProviderAndModel()
    {
        var a = new AgentMetrics("Claude", "sonnet-4", 100, 50, 0.0010m, TimeSpan.FromMilliseconds(200));
        var b = new AgentMetrics("AzureOpenAI", "gpt-4.1", 200, 100, 0.0040m, TimeSpan.FromMilliseconds(400));

        var sum = a.Add(b);

        sum.InputTokens.ShouldBe(300);
        sum.OutputTokens.ShouldBe(150);
        sum.CostUsd.ShouldBe(0.0050m);
        sum.Latency.ShouldBe(TimeSpan.FromMilliseconds(600));
        sum.Provider.ShouldBe("Claude+AzureOpenAI");
        sum.Model.ShouldBe("sonnet-4+gpt-4.1");
    }

    [Fact]
    public void Add_NullArg_Throws()
    {
        Should.Throw<ArgumentNullException>(() => AgentMetrics.Empty.Add(null!));
    }
}
