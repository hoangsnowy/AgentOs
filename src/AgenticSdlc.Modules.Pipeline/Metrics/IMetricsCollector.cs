// AgenticSdlc.Application/Metrics/IMetricsCollector.cs
// Sprint 4 — sink for RunMetric.

using System.Collections.Generic;

namespace AgenticSdlc.Modules.Pipeline.Metrics;

/// <summary>Receives RunMetric from agents → sinks it to storage (memory / CSV / OTel ...).</summary>
public interface IMetricsCollector
{
    /// <summary>Writes a single record.</summary>
    void Add(RunMetric metric);

    /// <summary>Snapshot of all written records (in-memory implementations).</summary>
    IReadOnlyList<RunMetric> Snapshot();
}
