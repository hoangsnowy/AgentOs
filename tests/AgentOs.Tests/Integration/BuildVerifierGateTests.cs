// #10 — the build verifier runs untrusted LLM-generated MSBuild projects (arbitrary code execution), so it
// is gated off by default outside Development. When disabled it must refuse WITHOUT spawning `dotnet build`.

using System.Threading;
using AgentOs.Modules.Integration;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Integration;

public sealed class BuildVerifierGateTests
{
    [Fact]
    public async Task VerifyFilesAsync_Disabled_ReturnsFailureWithoutBuilding()
    {
        var verifier = new BuildVerifier(NullLogger<BuildVerifier>.Instance, new BuildVerifierOptions(Enabled: false));

        var result = await verifier.VerifyFilesAsync(
            [new BuildVerifyFile("Program.cs", "class C {}")], CancellationToken.None);

        result.Success.ShouldBeFalse();
        result.ExitCode.ShouldBe(-1);
        result.Output.ShouldContain("disabled");
        result.ElapsedMilliseconds.ShouldBe(0); // never started a process
    }
}
