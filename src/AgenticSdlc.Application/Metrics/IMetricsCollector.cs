// AgenticSdlc.Application/Metrics/IMetricsCollector.cs
// Sprint 4 — sink cho RunMetric.

using System.Collections.Generic;

namespace AgenticSdlc.Application.Metrics;

/// <summary>Nhận RunMetric từ agent → sink ra storage (memory / CSV / OTel ...).</summary>
public interface IMetricsCollector
{
    /// <summary>Ghi 1 record.</summary>
    void Add(RunMetric metric);

    /// <summary>Snapshot tất cả record đã ghi (in-memory implementations).</summary>
    IReadOnlyList<RunMetric> Snapshot();
}
