// AgenticSdlc.Application/Metrics/MetricsContext.cs
// Sprint 4 — AsyncLocal ambient context cho KcId + RunId + Iteration. KC test set context trước khi RunAsync.

using System;
using System.Threading;

namespace AgenticSdlc.Application.Metrics;

/// <summary>Ambient context — set bằng <see cref="BeginScope"/>, đọc bằng <see cref="Current"/>.</summary>
public sealed class MetricsContext
{
    private static readonly AsyncLocal<MetricsContext?> _current = new();

    /// <summary>Context hiện tại (null nếu chưa set).</summary>
    public static MetricsContext? Current => _current.Value;

    /// <summary>RunId (1 pipeline call = 1 RunId).</summary>
    public required string RunId { get; init; }

    /// <summary>KcId (KC1..KC5 hoặc "ad-hoc").</summary>
    public required string KcId { get; init; }

    /// <summary>Iteration counter của QA loop. 0 cho call ngoài loop.</summary>
    public int Iteration { get; init; }

    /// <summary>Set context cho scope hiện tại. Dispose khôi phục context cũ.</summary>
    public static IDisposable BeginScope(string runId, string kcId, int iteration = 0)
    {
        var prev = _current.Value;
        _current.Value = new MetricsContext { RunId = runId, KcId = kcId, Iteration = iteration };
        return new Scope(prev);
    }

    /// <summary>Bump iteration counter trong scope hiện tại (tạo scope mới).</summary>
    public static IDisposable BeginIteration(int iteration)
    {
        var cur = _current.Value
            ?? throw new InvalidOperationException("BeginIteration cần BeginScope trước đó.");
        var prev = _current.Value;
        _current.Value = new MetricsContext { RunId = cur.RunId, KcId = cur.KcId, Iteration = iteration };
        return new Scope(prev);
    }

    private sealed class Scope(MetricsContext? previous) : IDisposable
    {
        public void Dispose() => _current.Value = previous;
    }
}
