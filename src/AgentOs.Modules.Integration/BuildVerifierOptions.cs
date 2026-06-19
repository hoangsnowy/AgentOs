// Gate + sandbox config for the build verifier. `dotnet build` runs LLM-generated source, and MSBuild
// executes arbitrary tasks during a build — i.e. an unsanitized build is remote code execution on the
// host. BuildInputSanitizer (Layer 1) removes the input vector; the runner (Layer 2) chooses the
// isolation boundary. OFF BY DEFAULT IN PRODUCTION until a real sandbox (Sandbox=Container) is selected;
// Development defaults on. `Integration:BuildVerifier:*` overrides each value. See ADR-0005.

namespace AgentOs.Modules.Integration;

/// <summary>Where the sandboxed build executes.</summary>
public enum BuildSandboxMode
{
    /// <summary>Child process on the host (Layer-1 hardening only; NOT OS-isolated). Development default.</summary>
    InProcess,

    /// <summary>Ephemeral no-egress container (`docker run` locally / ACA Jobs in cloud). Production-safe.</summary>
    Container,
}

/// <summary>Runtime config for <see cref="BuildVerifier"/>. When <see cref="Enabled"/> is false the verifier
/// refuses to run a build and returns a disabled result.</summary>
/// <param name="Enabled">Master gate. Default outside Development is <c>false</c>.</param>
/// <param name="Sandbox">Isolation host for the build.</param>
/// <param name="ContainerImage">Image used by the container runner (and the ACA Job). A dotnet SDK image.</param>
/// <param name="CpuLimit">CPU quota for the container (<c>--cpus</c>).</param>
/// <param name="MemoryLimit">Memory quota for the container (<c>--memory</c>, e.g. <c>1g</c>).</param>
/// <param name="PidsLimit">Process-count quota for the container (<c>--pids-limit</c>).</param>
/// <param name="TimeoutSeconds">Hard cap on build duration; the process tree is killed on overrun.</param>
public sealed record BuildVerifierOptions(
    bool Enabled,
    BuildSandboxMode Sandbox = BuildSandboxMode.InProcess,
    string ContainerImage = "mcr.microsoft.com/dotnet/sdk:10.0",
    double CpuLimit = 2.0,
    string MemoryLimit = "1g",
    int PidsLimit = 256,
    int TimeoutSeconds = 90);
