// ADR-0005 Layer 2 — the seam that lets the build run on different isolation hosts. BuildVerifier owns
// input hardening (Layer 1: reject build-control files, synthesize the project, clear the feed); the
// runner owns *where* `dotnet build` then executes — in-process (Dev) or inside an ephemeral, no-egress
// container (Production / ACA Jobs). Governance (IToolGateway → policy → evidence) wraps either.

using System.Threading;
using System.Threading.Tasks;

namespace AgentOs.Modules.Integration.Sandbox;

/// <summary>Executes <c>dotnet build</c> over an already-prepared, sanitized scratch directory and
/// returns the build outcome. Implementations differ only in the isolation boundary they impose.</summary>
public interface ISandboxedBuildRunner
{
    /// <summary>Run the build over <paramref name="workDir"/> (which already contains the sanitized source
    /// set, the synthesized project, and the cleared <c>nuget.config</c>).</summary>
    Task<BuildVerifyResult> RunAsync(string workDir, CancellationToken ct);
}
