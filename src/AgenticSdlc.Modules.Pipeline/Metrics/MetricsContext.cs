// AgenticSdlc.Application/Metrics/MetricsContext.cs
// Sprint 4 — AsyncLocal ambient context for KcId + RunId + Iteration. KC tests set the context before RunAsync.

using System;
using System.Threading;

namespace AgenticSdlc.Modules.Pipeline.Metrics;

/// <summary>Ambient context — set via <see cref="BeginScope"/>, read via <see cref="Current"/>.</summary>
public sealed class MetricsContext
{
    private static readonly AsyncLocal<MetricsContext?> _current = new();

    /// <summary>Current context (null if not set).</summary>
    public static MetricsContext? Current => _current.Value;

    /// <summary>RunId (one pipeline call = one RunId).</summary>
    public required string RunId { get; init; }

    /// <summary>KcId (KC1..KC5 or "ad-hoc").</summary>
    public required string KcId { get; init; }

    /// <summary>QA loop iteration counter. 0 for calls outside the loop.</summary>
    public int Iteration { get; init; }

    /// <summary>Sets the context for the current scope. Disposing restores the previous context.</summary>
    public static IDisposable BeginScope(string runId, string kcId, int iteration = 0)
    {
        var prev = _current.Value;
        _current.Value = new MetricsContext { RunId = runId, KcId = kcId, Iteration = iteration };
        return new Scope(prev);
    }

    /// <summary>Bumps the iteration counter within the current scope (creates a new scope).</summary>
    public static IDisposable BeginIteration(int iteration)
    {
        var cur = _current.Value
            ?? throw new InvalidOperationException("BeginIteration requires a prior BeginScope.");
        var prev = _current.Value;
        _current.Value = new MetricsContext { RunId = cur.RunId, KcId = cur.KcId, Iteration = iteration };
        return new Scope(prev);
    }

    private sealed class Scope(MetricsContext? previous) : IDisposable
    {
        public void Dispose() => _current.Value = previous;
    }
}
