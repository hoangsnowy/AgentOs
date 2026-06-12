// AgentOs.Infrastructure/Metrics/InMemoryMetricsCollector.cs
// Sprint 4 — simple, thread-safe, used for test + dev.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using AgentOs.Modules.Pipeline.Metrics;

namespace AgentOs.Modules.Pipeline.Metrics;

/// <summary>Stores RunMetric in a <see cref="ConcurrentQueue{T}"/>, bounded to the newest
/// <see cref="MaxRecords"/> entries — a long-lived singleton must not grow without limit
/// (durable history is the Postgres run store; this is the in-process working set).</summary>
public sealed class InMemoryMetricsCollector : IMetricsCollector
{
    /// <summary>Cap on retained metrics; oldest entries are dropped first.</summary>
    public const int MaxRecords = 10_000;

    private readonly ConcurrentQueue<RunMetric> _records = new();

    /// <inheritdoc />
    public void Add(RunMetric metric)
    {
        System.ArgumentNullException.ThrowIfNull(metric);
        _records.Enqueue(metric);
        while (_records.Count > MaxRecords && _records.TryDequeue(out _))
        {
            // Drop-oldest until back under the cap.
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<RunMetric> Snapshot() => _records.ToList();

    /// <summary>Reset (used for test cleanup).</summary>
    public void Clear()
    {
        while (_records.TryDequeue(out _))
        {
            // Drain loop — TryDequeue does the work; the body is intentionally empty.
        }
    }
}
