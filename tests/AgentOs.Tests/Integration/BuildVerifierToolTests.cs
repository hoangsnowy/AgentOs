// Epic E2 — BuildVerifierTool input/output contract + delegation to IBuildVerifier.

using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Tools;
using AgentOs.Modules.Integration;
using AgentOs.Modules.Integration.Tools;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Integration;

public sealed class BuildVerifierToolTests
{
    [Fact]
    public void Definition_ExposesNameAndSchema()
    {
        var tool = new BuildVerifierTool(Substitute.For<IBuildVerifier>());

        tool.Definition.Name.ShouldBe("build_verifier");
        tool.Definition.JsonInputSchema.ShouldContain("\"files\"");
    }

    [Fact]
    public async Task Invoke_ValidPayload_DelegatesToVerifierAndReturnsSuccessJson()
    {
        var verifier = Substitute.For<IBuildVerifier>();
        verifier.VerifyFilesAsync(Arg.Any<IEnumerable<BuildVerifyFile>>(), Arg.Any<CancellationToken>())
            .Returns(new BuildVerifyResult(Success: true, ExitCode: 0, Output: "Build succeeded.", ElapsedMilliseconds: 42));
        var tool = new BuildVerifierTool(verifier);

        var req = new ToolInvocationRequest(
            ToolName: "build_verifier",
            CallId: "call-1",
            Input: """{"files":[{"path":"Program.cs","content":"class P{static void Main(){}}"}]}""",
            TenantId: "tenant-1");

        var result = await tool.InvokeAsync(req);

        result.IsError.ShouldBeFalse();
        result.CallId.ShouldBe("call-1");
        var json = JsonDocument.Parse(result.Output);
        json.RootElement.GetProperty("success").GetBoolean().ShouldBeTrue();
        json.RootElement.GetProperty("exit_code").GetInt32().ShouldBe(0);
        json.RootElement.GetProperty("elapsed_ms").GetInt64().ShouldBe(42);
        await verifier.Received(1).VerifyFilesAsync(
            Arg.Is<IEnumerable<BuildVerifyFile>>(f => System.Linq.Enumerable.Count(f!) == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_BuildFails_ReturnsErrorWithOutput()
    {
        var verifier = Substitute.For<IBuildVerifier>();
        verifier.VerifyFilesAsync(Arg.Any<IEnumerable<BuildVerifyFile>>(), Arg.Any<CancellationToken>())
            .Returns(new BuildVerifyResult(Success: false, ExitCode: 1, Output: "CS1002: ;", ElapsedMilliseconds: 100));
        var tool = new BuildVerifierTool(verifier);

        var result = await tool.InvokeAsync(new ToolInvocationRequest(
            "build_verifier", "call-2",
            """{"files":[{"path":"Program.cs","content":"class P{}"}]}""",
            "tenant-1"));

        result.IsError.ShouldBeTrue();
        result.ErrorMessage!.ShouldContain("CS1002");
    }

    [Fact]
    public async Task Invoke_InvalidJson_ReturnsParseError()
    {
        var tool = new BuildVerifierTool(Substitute.For<IBuildVerifier>());

        var result = await tool.InvokeAsync(new ToolInvocationRequest(
            "build_verifier", "call-3", "not-json", "tenant-1"));

        result.IsError.ShouldBeTrue();
        result.ErrorMessage!.ShouldContain("not valid JSON");
    }

    [Fact]
    public async Task Invoke_MissingFiles_ReturnsValidationError()
    {
        var tool = new BuildVerifierTool(Substitute.For<IBuildVerifier>());

        var result = await tool.InvokeAsync(new ToolInvocationRequest(
            "build_verifier", "call-4", "{}", "tenant-1"));

        result.IsError.ShouldBeTrue();
        result.ErrorMessage!.ShouldContain("'files'");
    }

    [Fact]
    public async Task Invoke_FilesArrayWithBlankPath_ReturnsError()
    {
        var tool = new BuildVerifierTool(Substitute.For<IBuildVerifier>());

        var result = await tool.InvokeAsync(new ToolInvocationRequest(
            "build_verifier", "call-5",
            """{"files":[{"path":"","content":"x"}]}""",
            "tenant-1"));

        result.IsError.ShouldBeTrue();
        result.ErrorMessage!.ShouldContain("non-empty path");
    }
}
