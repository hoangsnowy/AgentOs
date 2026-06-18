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
    private readonly BuildVerifierOptions _options;

    /// <summary>Initializes the verifier with a logger and the enable/disable gate.</summary>
    public BuildVerifier(ILogger<BuildVerifier> logger, BuildVerifierOptions options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
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

        // Gate: refuse to run a build (which executes arbitrary MSBuild tasks from LLM-generated projects)
        // when disabled — the default outside Development until a sandboxed runner exists.
        if (!_options.Enabled)
        {
            _logger.LogWarning("Build verification is disabled on this host (Integration:BuildVerifier:Enabled=false); refusing to run `dotnet build`.");
            return new BuildVerifyResult(false, -1,
                "Build verification is disabled on this host. It runs untrusted generated code and is off by "
                + "default outside Development; enable Integration:BuildVerifier:Enabled only where builds are sandboxed.",
                0);
        }

        var workDir = Path.Join(Path.GetTempPath(), "agentic-sdlc-build-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(workDir);
        var workDirFull = Path.GetFullPath(workDir);
        _logger.LogInformation("Build verifier scratch dir: {Dir}", workDir);

        var stopwatch = Stopwatch.StartNew();
        var rejected = 0;
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
                // ADR-0005 Layer 1: the model may contribute SOURCE files only. A project/solution/import/
                // config/response file lets it inject MSBuild tasks (RCE) or repoint restore at a hostile
                // feed — drop it and never write it; the verifier synthesizes its own project below.
                if (BuildInputSanitizer.IsBuildControlFile(path))
                {
                    rejected++;
                    _logger.LogWarning("Rejected a generated build-control file (project/import/config) — only source files are compiled.");
                    continue;
                }
                // Generated file paths come from the LLM — normalise and confine them to the scratch
                // dir so a rooted or ../-laden path cannot write outside the sandbox (path traversal).
                var dest = Path.GetFullPath(Path.Join(workDir, path));
                if (!dest.StartsWith(workDirFull + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    rejected++;
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

            // 2. ALWAYS synthesize the project ourselves — a single zero-dependency SDK-style net10.0
            // project (no PackageReference/Analyzer/ProjectReference, so no build-time package or source
            // generator can be introduced). Any model-authored .csproj was rejected above.
            await File.WriteAllTextAsync(Path.Join(workDir, "AgentOsGenerated.csproj"),
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

            // 2b. Cut the feed: a project-local nuget.config that clears all package sources, so even if
            // something triggers a restore it cannot reach a remote (or attacker-supplied) feed. A
            // zero-dependency net10.0 project resolves its framework packs from the SDK install, offline.
            await File.WriteAllTextAsync(Path.Join(workDir, "nuget.config"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources>
                    <clear />
                  </packageSources>
                </configuration>
                """, ct).ConfigureAwait(false);

            // 3. Run `dotnet build` with timeout + cancellation.
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                // --no-incremental keeps the build self-contained; node reuse off so no MSBuild worker
                // survives the scratch dir. (True isolation needs a sandboxed runner — this is defense in depth.)
                Arguments = "build --nologo -v q --no-incremental -nodeReuse:false",
                WorkingDirectory = workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            // Harden the child environment: opt out of telemetry/first-run, kill node reuse, and redirect
            // HOME/USERPROFILE into the scratch dir so a malicious build can't read the server's user
            // profile or a user-level NuGet.config (private feeds / credentials).
            psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            psi.Environment["DOTNET_NOLOGO"] = "1";
            psi.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
            psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
            psi.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";
            psi.Environment["HOME"] = workDirFull;
            psi.Environment["USERPROFILE"] = workDirFull;

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
                // Killing the process tears down the pipes; drain the stdout/stderr read tasks so they complete
                // BEFORE the linked CTS is disposed at method exit. Leaving them running would have them touch a
                // disposed cts.Token, surfacing as an unobserved ObjectDisposedException. Output is discarded here.
                await ObserveAsync(outTask).ConfigureAwait(false);
                await ObserveAsync(errTask).ConfigureAwait(false);
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
            if (rejected > 0)
            {
                // Tell the caller the model's build-control files were dropped, so a "build succeeded"
                // is never mistaken for "the model's exact project built".
                combined = $"[build_verifier] Rejected {rejected} build-control file(s) "
                    + "(project/solution/import/config/response) — only source files are compiled, "
                    + "against a synthesized zero-dependency project.\n" + combined;
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

    // Awaits a read task only to observe its completion/exception so the linked CTS can be disposed safely.
    // On the timeout path the captured output is unused; cancellation + a torn pipe are the expected outcomes.
    private static async Task ObserveAsync(Task<string> task)
    {
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { /* expected — the build was cancelled */ }
        catch (IOException) { /* expected — the kill tore down the pipe */ }
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
