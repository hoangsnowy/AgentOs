// ADR-0005 Layer 2 — runs the build inside an ephemeral, no-egress container via `docker run`. This is
// the OS-isolation host: even a defense that slips past Layer-1 input hardening cannot reach the server's
// host, network, or filesystem. Selected by Integration:BuildVerifier:Sandbox=Container; the same image
// runs as an Azure Container Apps Job in cloud (see docs/adr/0005-build-verifier-sandbox.md).

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AgentOs.Modules.Integration.Sandbox;

internal sealed class ContainerBuildRunner : ISandboxedBuildRunner
{
    private readonly BuildVerifierOptions _options;

    public ContainerBuildRunner(BuildVerifierOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public Task<BuildVerifyResult> RunAsync(string workDir, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // ArgumentList (not a joined string) so a hostile path can never break out of the argv.
        foreach (var arg in ContainerCommand.Build(_options, workDir))
        {
            psi.ArgumentList.Add(arg);
        }
        // Allow headroom over the in-container build timeout for image pull / container start.
        return BoundedProcess.RunAsync(
            psi, _options.TimeoutSeconds + 30, BoundedProcess.DefaultMaxOutputBytes, ct);
    }
}
