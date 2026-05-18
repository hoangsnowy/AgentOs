// AgenticSdlc.Infrastructure/Metrics/InMemoryMetricsCollector.cs
// Sprint 4 — đơn giản, thread-safe, dùng cho test + dev.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using AgenticSdlc.Application.Metrics;

namespace AgenticSdlc.Infrastructure.Metrics;

/// <summary>Lưu RunMetric trong <see cref="ConcurrentQueue{T}"/>.</summary>
public sealed class InMemoryMetricsCollector : IMetricsCollector
{
    private readonly ConcurrentQueue<RunMetric> _records = new();

    /// <inheritdoc />
    public void Add(RunMetric metric)
    {
        System.ArgumentNullException.ThrowIfNull(metric);
        _records.Enqueue(metric);
    }

    /// <inheritdoc />
    public IReadOnlyList<RunMetric> Snapshot() => _records.ToList();

    /// <summary>Reset (dùng cho test cleanup).</summary>
    public void Clear()
    {
        while (_records.TryDequeue(out _))
        {
        }
    }
}
