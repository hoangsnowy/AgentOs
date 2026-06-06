// AgentOs.Infrastructure/Integration/BuildVerifier.cs
// IBuildVerifier impl: writes the pipeline-generated files to a temp dir, ensures a .csproj exists,
// runs `dotnet build` with a hard timeout, captures stdout/stderr, then cleans up.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Modules.Integration;
using AgentOs.Domain.Pipeline;
using Microsoft.Extensions.Logging;

namespace AgentOs.Modules.Integration;

/// <inheritdoc cref="IBuildVerifier"/>
public sealed class BuildVerifier : IBuildVerifier
{
    /// <summary>Hard cap on the build duration (seconds) — kills the process if it overruns.</summary>
    public const int BuildTimeoutSeconds = 90;

    /// <summary>Output is truncated to this many bytes so the UI/log payload stays bounded.</summary>
    public const int MaxOutputBytes = 8 * 1024;

    private readonly ILogger<BuildVerifier> _logger;

    /// <summary>Initializes the verifier with a logger.</summary>
    public BuildVerifier(ILogger<BuildVerifier> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<BuildVerifyResult> VerifyAsync(PipelineResult result, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(result);

        var files = new List<BuildVerifyFile>();
        if (result.Code?.Files is not null)
        {
            files.AddRange(result.Code.Files.Select(f => new BuildVerifyFile(f.Path, f.Content)));
        }
        if (result.Tests?.Files is not null)
        {
            files.AddRange(result.Tests.Files.Select(f => new BuildVerifyFile(f.Path, f.Content)));
        }
        return VerifyFilesAsync(files, ct);
    }

    /// <inheritdoc />
    public async Task<BuildVerifyResult> VerifyFilesAsync(IEnumerable<BuildVerifyFile> files, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(files);

        var workDir = Path.Join(Path.GetTempPath(), "agentic-sdlc-build-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(workDir);
        var workDirFull = Path.GetFullPath(workDir);
        _logger.LogInformation("Build verifier scratch dir: {Dir}", workDir);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            foreach (var file in files)
            {
                var path = file.Path;
                var content = file.Content;
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }
                // Generated file paths come from the LLM — normalise and confine them to the scratch
                // dir so a rooted or ../-laden path cannot write outside the sandbox (path traversal).
                var dest = Path.GetFullPath(Path.Join(workDir, path));
                if (!dest.StartsWith(workDirFull + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    _logger.LogWarning("Skipping a generated file whose path escapes the build sandbox.");
                    continue;
                }
                var destDir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }
                await File.WriteAllTextAsync(dest, content, ct).ConfigureAwait(false);
            }

            // 2. Ensure a project file exists (the Coding agent often emits scattered .cs files only).
            if (Directory.GetFiles(workDir, "*.csproj", SearchOption.AllDirectories).Length == 0)
            {
                var csproj = Path.Join(workDir, "AgentOsGenerated.csproj");
                await File.WriteAllTextAsync(csproj,
                    """
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup>
                        <TargetFramework>net10.0</TargetFramework>
                        <Nullable>enable</Nullable>
                        <ImplicitUsings>enable</ImplicitUsings>
                        <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
                      </PropertyGroup>
                    </Project>
                    """, ct).ConfigureAwait(false);
            }

            // 3. Run `dotnet build` with timeout + cancellation.
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "build --nologo -v q",
                WorkingDirectory = workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start dotnet build process.");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(BuildTimeoutSeconds));

            var outTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
            var errTask = proc.StandardError.ReadToEndAsync(cts.Token);

            try
            {
                await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(proc);
                stopwatch.Stop();
                return new BuildVerifyResult(false, -1,
                    $"Build cancelled / timed out after {BuildTimeoutSeconds}s.", stopwatch.ElapsedMilliseconds);
            }

            var stdout = await outTask.ConfigureAwait(false);
            var stderr = await errTask.ConfigureAwait(false);
            var combined = (stdout + (string.IsNullOrEmpty(stderr) ? "" : "\n" + stderr)).Trim();
            if (combined.Length > MaxOutputBytes)
            {
                combined = combined[..MaxOutputBytes] + "\n... (truncated)";
            }

            stopwatch.Stop();
            return new BuildVerifyResult(proc.ExitCode == 0, proc.ExitCode, combined, stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            // Best-effort cleanup; don't surface errors here.
            try { Directory.Delete(workDir, recursive: true); }
            catch (IOException ex) { _logger.LogWarning(ex, "Failed to delete scratch dir {Dir}", workDir); }
            catch (UnauthorizedAccessException ex) { _logger.LogWarning(ex, "Failed to delete scratch dir {Dir}", workDir); }
        }
    }

    private static void TryKill(Process proc)
    {
        try { if (!proc.HasExited) { proc.Kill(entireProcessTree: true); } }
        // Best-effort kill: the process may have already exited (InvalidOperationException) or the OS
        // refused the kill (Win32Exception). Nothing we can do — swallow and let cleanup proceed.
        catch (InvalidOperationException ex) { _ = ex.Message; }
        catch (System.ComponentModel.Win32Exception ex) { _ = ex.Message; }
        catch (NotSupportedException ex) { _ = ex.Message; }
    }
}
