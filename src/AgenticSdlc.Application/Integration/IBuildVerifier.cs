// AgenticSdlc.Application/Integration/IBuildVerifier.cs
// Writes the generated code + tests from a pipeline run to a temporary directory and runs `dotnet build`
// to confirm the agent's output actually compiles. Captures stdout/stderr + exit code.

using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Domain.Pipeline;

namespace AgenticSdlc.Application.Integration;

/// <summary>
/// Verifies that a pipeline-generated <see cref="PipelineResult"/> compiles. The implementation writes the
/// generated files to a temp directory, ensures a project file exists, and runs <c>dotnet build</c>.
/// </summary>
/// <remarks>
/// Not a real sandbox — code runs in the host process with the same permissions as the Web server.
/// A proper sandbox (container / capability-restricted runtime) is on the roadmap.
/// </remarks>
public interface IBuildVerifier
{
    /// <summary>Runs <c>dotnet build</c> on the generated files; returns success + captured output.</summary>
    Task<BuildVerifyResult> VerifyAsync(PipelineResult result, CancellationToken ct);
}

/// <summary>Result of <see cref="IBuildVerifier.VerifyAsync"/>.</summary>
/// <param name="Success"><c>true</c> when the build exited with code 0.</param>
/// <param name="ExitCode">Process exit code from <c>dotnet build</c>.</param>
/// <param name="Output">Combined stdout + stderr of the build (truncated to 8 KB).</param>
/// <param name="ElapsedMilliseconds">Wall-clock time the build took.</param>
public sealed record BuildVerifyResult(bool Success, int ExitCode, string Output, long ElapsedMilliseconds);
