// AgenticSdlc.Infrastructure/Metrics/CsvMetricsCollector.cs
// Sprint 4 — append-only CSV sink. Header auto-created for a new file. Thread-safe via a lock.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AgenticSdlc.Application.Metrics;

namespace AgenticSdlc.Infrastructure.Metrics;

/// <summary>Sinks RunMetric to a CSV file (append-only).</summary>
public sealed class CsvMetricsCollector : IMetricsCollector
{
    /// <summary>Header column order (matches <see cref="ToRow"/>).</summary>
    public const string Header =
        "timestamp,runId,kcId,iteration,agentName,model,provider,tokensIn,tokensOut,latencyMs,costUsd,success,errorMessage";

    private readonly object _lock = new();
    private readonly string _filePath;
    private readonly List<RunMetric> _buffer = [];

    /// <summary>Initializes with a CSV path (creates the parent directory if it does not exist).</summary>
    public CsvMetricsCollector(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    /// <summary>Path to the CSV file.</summary>
    public string FilePath => _filePath;

    /// <inheritdoc />
    public void Add(RunMetric metric)
    {
        ArgumentNullException.ThrowIfNull(metric);
        lock (_lock)
        {
            _buffer.Add(metric);
            var newFile = !File.Exists(_filePath);
            using var writer = new StreamWriter(_filePath, append: true, Encoding.UTF8);
            if (newFile)
            {
                writer.WriteLine(Header);
            }
            writer.WriteLine(ToRow(metric));
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<RunMetric> Snapshot()
    {
        lock (_lock)
        {
            return _buffer.ToList();
        }
    }

    /// <summary>Formats a single CSV row (escapes ", , \n inside errorMessage).</summary>
    public static string ToRow(RunMetric m)
    {
        var inv = CultureInfo.InvariantCulture;
        return string.Join(',',
            m.Timestamp.ToString("O", inv),
            Escape(m.RunId),
            Escape(m.KcId),
            m.Iteration.ToString(inv),
            Escape(m.AgentName),
            Escape(m.Model),
            Escape(m.Provider),
            m.TokensIn.ToString(inv),
            m.TokensOut.ToString(inv),
            m.LatencyMs.ToString("F2", inv),
            m.CostUsd.ToString("F6", inv),
            m.Success ? "true" : "false",
            Escape(m.ErrorMessage ?? string.Empty));
    }

    private static string Escape(string s)
    {
        if (s.IndexOfAny([',', '"', '\n', '\r']) < 0)
        {
            return s;
        }
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
