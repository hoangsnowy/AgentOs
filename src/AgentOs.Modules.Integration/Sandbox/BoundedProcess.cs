// ADR-0005 — runs a child process with a hard timeout, captures stdout+stderr, byte-caps the output,
// and kills the whole process tree on overrun. Shared by both sandbox runners (the dotnet build, and
// the `docker run` wrapper). Extracted verbatim from the original in-process BuildVerifier logic.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AgentOs.Modules.Integration.Sandbox;

internal static class BoundedProcess
{
    /// <summary>Captured output is truncated to this many bytes so the UI/log payload stays bounded.</summary>
    internal const int DefaultMaxOutputBytes = 8 * 1024;

    /// <summary>Starts <paramref name="psi"/>, waits up to <paramref name="timeoutSeconds"/>, and returns the
    /// exit code + captured (capped) output + wall-clock. On timeout/cancellation the tree is killed and a
    /// failure result with exit code -1 is returned.</summary>
    internal static async Task<BuildVerifyResult> RunAsync(
        ProcessStartInfo psi, int timeoutSeconds, int maxOutputBytes, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process '{psi.FileName}'.");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var outTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
        var errTask = proc.StandardError.ReadToEndAsync(cts.Token);

        try
        {
            await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(proc);
            // Killing tears down the pipes; drain the reads so they complete BEFORE the linked CTS is
            // disposed at method exit (otherwise they touch a disposed token → unobserved ObjectDisposed).
            await ObserveAsync(outTask).ConfigureAwait(false);
            await ObserveAsync(errTask).ConfigureAwait(false);
            stopwatch.Stop();
            return new BuildVerifyResult(false, -1,
                $"Build cancelled / timed out after {timeoutSeconds}s.", stopwatch.ElapsedMilliseconds);
        }

        var stdout = await outTask.ConfigureAwait(false);
        var stderr = await errTask.ConfigureAwait(false);
        var combined = (stdout + (string.IsNullOrEmpty(stderr) ? "" : "\n" + stderr)).Trim();
        if (combined.Length > maxOutputBytes)
        {
            combined = combined[..maxOutputBytes] + "\n... (truncated)";
        }

        stopwatch.Stop();
        return new BuildVerifyResult(proc.ExitCode == 0, proc.ExitCode, combined, stopwatch.ElapsedMilliseconds);
    }

    private static async Task ObserveAsync(Task<string> task)
    {
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { /* expected — the process was cancelled */ }
        catch (IOException) { /* expected — the kill tore down the pipe */ }
    }

    private static void TryKill(Process proc)
    {
        try { if (!proc.HasExited) { proc.Kill(entireProcessTree: true); } }
        catch (InvalidOperationException ex) { _ = ex.Message; }
        catch (System.ComponentModel.Win32Exception ex) { _ = ex.Message; }
        catch (NotSupportedException ex) { _ = ex.Message; }
    }
}
