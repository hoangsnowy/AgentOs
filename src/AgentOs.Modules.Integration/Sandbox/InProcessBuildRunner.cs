// ADR-0005 — the Development-default runner. Runs `dotnet build` as a child process on the host with the
// Layer-1 hardening env (HOME/USERPROFILE redirected into the scratch dir). This is NOT OS-isolated — it
// shares the host kernel/network/fs — which is why build_verifier stays gated off by default in Production
// (Integration:BuildVerifier:Enabled) until the container runner is selected.

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace AgentOs.Modules.Integration.Sandbox;

internal sealed class InProcessBuildRunner : ISandboxedBuildRunner
{
    private readonly BuildVerifierOptions _options;

    public InProcessBuildRunner(BuildVerifierOptions options)
    {
        _options = options ?? throw new System.ArgumentNullException(nameof(options));
    }

    public Task<BuildVerifyResult> RunAsync(string workDir, CancellationToken ct)
    {
        var workDirFull = Path.GetFullPath(workDir);
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = SandboxDefaults.DotnetBuildArgs,
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var kv in SandboxDefaults.HardeningEnv)
        {
            psi.Environment[kv.Key] = kv.Value;
        }
        // Redirect HOME/USERPROFILE into the scratch dir so a build can't read the server's user profile
        // or a user-level NuGet.config (private feeds / credentials).
        psi.Environment["HOME"] = workDirFull;
        psi.Environment["USERPROFILE"] = workDirFull;

        return BoundedProcess.RunAsync(psi, _options.TimeoutSeconds, BoundedProcess.DefaultMaxOutputBytes, ct);
    }
}
