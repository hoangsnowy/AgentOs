// ADR-0005 Layer 2 — the `docker run` argument vector IS the isolation contract. These assert the
// security-critical flags are present (and dangerous ones absent) without needing a Docker daemon, the
// same way BuildInputSanitizerTests covers Layer 1's policy decision.

using System.Linq;
using AgentOs.Modules.Integration;
using AgentOs.Modules.Integration.Sandbox;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Integration;

public sealed class ContainerCommandTests
{
    private static string[] Cmd(BuildVerifierOptions? options = null) =>
        ContainerCommand.Build(options ?? new BuildVerifierOptions(Enabled: true), "/tmp/scratch").ToArray();

    [Fact]
    public void Build_DisablesEgress()
    {
        var cmd = Cmd();
        // --network none is the decisive isolation property: the build cannot reach the host or internet.
        Adjacent(cmd, "--network").ShouldBe("none");
    }

    [Fact]
    public void Build_HardensTheContainer()
    {
        var cmd = Cmd();
        cmd.ShouldContain("--read-only");
        cmd.ShouldContain("--rm");
        Adjacent(cmd, "--user").ShouldBe("1000:1000");
        Adjacent(cmd, "--cap-drop").ShouldBe("ALL");
        Adjacent(cmd, "--security-opt").ShouldBe("no-new-privileges");
    }

    [Fact]
    public void Build_AppliesResourceQuotas()
    {
        var cmd = Cmd(new BuildVerifierOptions(Enabled: true, CpuLimit: 1.5, MemoryLimit: "768m", PidsLimit: 128));
        Adjacent(cmd, "--cpus").ShouldBe("1.5");
        Adjacent(cmd, "--memory").ShouldBe("768m");
        Adjacent(cmd, "--pids-limit").ShouldBe("128");
    }

    [Fact]
    public void Build_NeverUsesDangerousFlags()
    {
        var cmd = Cmd();
        cmd.ShouldNotContain("--privileged");
        cmd.ShouldNotContain("host");        // no --network host / --pid host that would defeat isolation
        cmd.ShouldNotContain("--cap-add");
    }

    [Fact]
    public void Build_MountsScratchAndRunsDotnetBuild()
    {
        var cmd = Cmd();
        // the scratch dir is bind-mounted to /work and is the working dir
        cmd.ShouldContain(c => c.EndsWith(":/work:rw", System.StringComparison.Ordinal));
        Adjacent(cmd, "-w").ShouldBe("/work");
        // the image precedes the in-container command; the SDK image entrypoint isn't `dotnet`, so it
        // must be named explicitly before the build verb.
        var imageIdx = System.Array.IndexOf(cmd, "mcr.microsoft.com/dotnet/sdk:10.0");
        imageIdx.ShouldBeGreaterThan(0);
        cmd[imageIdx + 1].ShouldBe("dotnet");
        cmd[imageIdx + 2].ShouldBe("build");
    }

    [Fact]
    public void Build_RedirectsHomeToTmpfs()
    {
        var cmd = Cmd();
        cmd.ShouldContain("HOME=/home/build");
        cmd.ShouldContain(c => c.StartsWith("/home/build:rw", System.StringComparison.Ordinal));
    }

    // Returns the token immediately after the first occurrence of `flag`.
    private static string Adjacent(string[] cmd, string flag)
    {
        var i = System.Array.IndexOf(cmd, flag);
        i.ShouldBeGreaterThanOrEqualTo(0, $"flag '{flag}' not found");
        (i + 1).ShouldBeLessThan(cmd.Length);
        return cmd[i + 1];
    }
}
