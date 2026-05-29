// AgenticSdlc.Tests/Metrics/MetricsCollectorTests.cs
// Sprint 4 — 5 case basic + 1 CSV round-trip.

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using AgenticSdlc.Modules.Pipeline.Metrics;
using Shouldly;
using Xunit;

namespace AgenticSdlc.Tests.Metrics;

public class MetricsCollectorTests
{
    private static readonly string[] _expectedAgentOrder =
        ["RequirementAgent", "CodingAgent", "TestingAgent"];

    private static RunMetric Sample(string agent = "RequirementAgent", string kc = "KC1", int iter = 1, bool success = true)
        => new(
            RunId: "run-001",
            KcId: kc,
            Iteration: iter,
            AgentName: agent,
            Model: "mock-model",
            Provider: "Mock",
            TokensIn: 100,
            TokensOut: 50,
            LatencyMs: 234.56,
            CostUsd: 0.000123m,
            Success: success,
            ErrorMessage: success ? null : "boom",
            Timestamp: new DateTimeOffset(2026, 5, 18, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public void InMemory_AddOne_SnapshotReturnsOne()
    {
        var c = new InMemoryMetricsCollector();
        c.Add(Sample());
        c.Snapshot().Count.ShouldBe(1);
        c.Snapshot()[0].KcId.ShouldBe("KC1");
    }

    [Fact]
    public void InMemory_AddMany_SnapshotPreservesOrder()
    {
        var c = new InMemoryMetricsCollector();
        c.Add(Sample(agent: "RequirementAgent"));
        c.Add(Sample(agent: "CodingAgent"));
        c.Add(Sample(agent: "TestingAgent"));
        var snap = c.Snapshot();
        snap.Count.ShouldBe(3);
        snap.Select(m => m.AgentName).ShouldBe(_expectedAgentOrder);
    }

    [Fact]
    public void InMemory_Clear_Empties()
    {
        var c = new InMemoryMetricsCollector();
        c.Add(Sample());
        c.Add(Sample());
        c.Clear();
        c.Snapshot().ShouldBeEmpty();
    }

    [Fact]
    public void MetricsContext_BeginScope_SetsCurrent()
    {
        MetricsContext.Current.ShouldBeNull();
        using (MetricsContext.BeginScope("r1", "KC2", 5))
        {
            MetricsContext.Current.ShouldNotBeNull();
            MetricsContext.Current!.KcId.ShouldBe("KC2");
            MetricsContext.Current.Iteration.ShouldBe(5);
        }
        MetricsContext.Current.ShouldBeNull();
    }

    [Fact]
    public void RunMetricFactory_FromResponse_UsesAmbientContext()
    {
        using var _ = MetricsContext.BeginScope("run-xyz", "KC3", 7);
        var resp = new global::AgenticSdlc.Domain.Llm.LlmResponse(
            Content: "{}", InputTokens: 10, OutputTokens: 5,
            CostUsd: 0.0001m, Latency: TimeSpan.FromMilliseconds(99),
            Model: "m", Provider: "Mock");
        var rm = RunMetricFactory.From(resp, "RequirementAgent", success: true, errorMessage: null);
        rm.RunId.ShouldBe("run-xyz");
        rm.KcId.ShouldBe("KC3");
        rm.Iteration.ShouldBe(7);
        rm.AgentName.ShouldBe("RequirementAgent");
        rm.TokensIn.ShouldBe(10);
        rm.Success.ShouldBeTrue();
    }

    [Fact]
    public void Csv_RoundTrip_HeaderPlusRowsReadable()
    {
        var path = Path.Combine(Path.GetTempPath(), $"metrics_{Guid.NewGuid():N}.csv");
        try
        {
            var c = new CsvMetricsCollector(path);
            c.Add(Sample(kc: "KC1", iter: 1));
            c.Add(Sample(kc: "KC1", iter: 2, success: false));
            c.Add(Sample(kc: "KC2", iter: 1));

            File.Exists(path).ShouldBeTrue();
            var lines = File.ReadAllLines(path);
            lines.Length.ShouldBe(4); // header + 3 rows
            lines[0].ShouldBe(CsvMetricsCollector.Header);
            lines[1].ShouldContain("KC1");
            lines[1].ShouldContain("true");
            lines[2].ShouldContain("false");
            lines[2].ShouldContain("boom");
            lines[3].ShouldContain("KC2");

            // Tokens + cost formatted invariant.
            lines[1].ShouldContain("100");
            lines[1].ShouldContain("0.000123");

            c.Snapshot().Count.ShouldBe(3);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
