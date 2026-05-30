// Epic E1 — Invocation request validation + result convenience factories.

using AgentOs.Domain.Tools;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Tools;

public sealed class ToolInvocationTests
{
    [Fact]
    public void Request_Validate_AllFieldsValid_DoesNotThrow()
    {
        var req = new ToolInvocationRequest("echo", "call-1", "{}", "tenant-1", RunId: "run-1");
        Should.NotThrow(() => req.Validate());
    }

    [Theory]
    [InlineData("", "call-1", "{}", "tenant-1")]
    [InlineData("echo", "", "{}", "tenant-1")]
    [InlineData("echo", "call-1", "{}", "")]
    public void Request_Validate_MissingRequired_Throws(string toolName, string callId, string input, string tenantId)
    {
        var req = new ToolInvocationRequest(toolName, callId, input, tenantId);
        Should.Throw<System.ArgumentException>(() => req.Validate());
    }

    [Fact]
    public void Request_Validate_NullInput_Throws()
    {
        var req = new ToolInvocationRequest("echo", "call-1", null!, "tenant-1");
        Should.Throw<System.ArgumentException>(() => req.Validate());
    }

    [Fact]
    public void Result_Success_BuildsNonErrorResult()
    {
        var result = ToolInvocationResult.Success("call-1", "ok");

        result.CallId.ShouldBe("call-1");
        result.Output.ShouldBe("ok");
        result.IsError.ShouldBeFalse();
        result.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public void Result_Error_BuildsErrorResultWithMessage()
    {
        var result = ToolInvocationResult.Error("call-1", "build failed");

        result.CallId.ShouldBe("call-1");
        result.Output.ShouldBe(string.Empty);
        result.IsError.ShouldBeTrue();
        result.ErrorMessage.ShouldBe("build failed");
    }
}
