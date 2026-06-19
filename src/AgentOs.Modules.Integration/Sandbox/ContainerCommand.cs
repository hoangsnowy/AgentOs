// ADR-0005 Layer 2 — builds the `docker run` argument vector for an ephemeral, locked-down build
// container. Pure + side-effect-free so the isolation flags are unit-testable without a Docker daemon.
// The SAME image + flags are used by ACA Jobs in cloud (the job runs this image with egress disabled and
// the quotas applied at the platform level); locally we apply them per `docker run`.

using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace AgentOs.Modules.Integration.Sandbox;

internal static class ContainerCommand
{
    /// <summary>Mount point for the scratch dir inside the container.</summary>
    internal const string ContainerWork = "/work";

    /// <summary>Writable HOME (a tmpfs) so the SDK can write its caches without a writable root fs.</summary>
    internal const string ContainerHome = "/home/build";

    /// <summary>Composes <c>docker run …</c> for one ephemeral build. Isolation: no network, read-only root
    /// fs (writable only via tmpfs + the bind-mounted scratch dir), non-root uid, dropped capabilities,
    /// no-new-privileges, and CPU/memory/PID quotas. The container is removed on exit (<c>--rm</c>).</summary>
    internal static IReadOnlyList<string> Build(BuildVerifierOptions options, string workDir)
    {
        System.ArgumentNullException.ThrowIfNull(options);
        var args = new List<string>
        {
            "run", "--rm",
            "--network", "none",                 // no egress — the decisive isolation property
            "--read-only",                        // immutable root fs
            "--user", "1000:1000",                // non-root
            "--cap-drop", "ALL",                  // no Linux capabilities
            "--security-opt", "no-new-privileges",
            "--cpus", options.CpuLimit.ToString(CultureInfo.InvariantCulture),
            "--memory", options.MemoryLimit,
            "--pids-limit", options.PidsLimit.ToString(CultureInfo.InvariantCulture),
            // World-writable (mode=1777) so the non-root uid can write — tmpfs is root-owned otherwise.
            "--tmpfs", "/tmp:rw,exec,size=512m,mode=1777",  // writable scratch the toolchain expects
            "--tmpfs", ContainerHome + ":rw,size=128m,mode=1777",
        };
        foreach (var kv in SandboxDefaults.HardeningEnv)
        {
            args.Add("-e");
            args.Add($"{kv.Key}={kv.Value}");
        }
        args.Add("-e");
        args.Add($"HOME={ContainerHome}");
        args.Add("-v");
        args.Add($"{Path.GetFullPath(workDir)}:{ContainerWork}:rw");
        args.Add("-w");
        args.Add(ContainerWork);
        args.Add(options.ContainerImage);
        // The dotnet SDK image's entrypoint is not `dotnet`, so name it explicitly as the command.
        args.Add("dotnet");
        args.AddRange(SandboxDefaults.DotnetBuildArgv);
        return args;
    }
}
