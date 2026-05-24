// AgenticSdlc.Tests/Llm/LlmRequestTests.cs
// Sprint 1 — Unit tests for the LlmRequest record.

using System;
using AgenticSdlc.Domain.Llm;
using Shouldly;
using Xunit;

namespace AgenticSdlc.Tests.Llm;

public class LlmRequestTests
{
    [Fact]
    public void Constructor_DefaultValues_SetExpectedDefaults()
    {
        // Arrange & Act
        var req = new LlmRequest(SystemPrompt: "sys", UserPrompt: "hello", Model: "claude-sonnet-4");

        // Assert
        req.SystemPrompt.ShouldBe("sys");
        req.UserPrompt.ShouldBe("hello");
        req.Model.ShouldBe("claude-sonnet-4");
        req.Temperature.ShouldBe(0.0);
        req.MaxTokens.ShouldBe(4096);
        req.JsonSchema.ShouldBeNull();
    }

    [Fact]
    public void Validate_AllFieldsValid_DoesNotThrow()
    {
        // Arrange
        var req = new LlmRequest("sys", "user", "gpt-4.1", 0.5, 1024, null);

        // Act & Assert
        Should.NotThrow(() => req.Validate());
    }

    [Theory]
    [InlineData("sys", "", "model")]             // empty user
    [InlineData("sys", "   ", "model")]          // whitespace user
    [InlineData("sys", "user", "")]              // empty model
    public void Validate_InvalidInput_Throws(string sys, string user, string model)
    {
        // Arrange
        var req = new LlmRequest(sys, user, model);

        // Act & Assert
        Should.Throw<ArgumentException>(() => req.Validate());
    }

    [Fact]
    public void Records_WithSameValues_ShouldBeEqual()
    {
        // Arrange
        var a = new LlmRequest("sys", "user", "model", 0.2, 2048, "schema");
        var b = new LlmRequest("sys", "user", "model", 0.2, 2048, "schema");

        // Act & Assert
        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }
}
