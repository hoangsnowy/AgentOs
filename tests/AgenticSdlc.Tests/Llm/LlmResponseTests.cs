// AgenticSdlc.Tests/Llm/LlmResponseTests.cs
// Sprint 1 — Unit tests for the LlmResponse record.

using System;
using AgenticSdlc.Domain.Llm;
using Shouldly;
using Xunit;

namespace AgenticSdlc.Tests.Llm;

public class LlmResponseTests
{
    [Fact]
    public void Constructor_AllArgs_ExposesProperties()
    {
        // Arrange
        var latency = TimeSpan.FromMilliseconds(123);

        // Act
        var res = new LlmResponse(
            Content: "hello",
            InputTokens: 10,
            OutputTokens: 20,
            CostUsd: 0.0005m,
            Latency: latency,
            Model: "claude-sonnet-4",
            Provider: "Claude");

        // Assert
        res.Content.ShouldBe("hello");
        res.InputTokens.ShouldBe(10);
        res.OutputTokens.ShouldBe(20);
        res.CostUsd.ShouldBe(0.0005m);
        res.Latency.ShouldBe(latency);
        res.Model.ShouldBe("claude-sonnet-4");
        res.Provider.ShouldBe("Claude");
    }

    [Fact]
    public void Records_WithSameValues_ShouldBeEqual()
    {
        // Arrange
        var t = TimeSpan.FromSeconds(1);
        var a = new LlmResponse("x", 1, 2, 0.01m, t, "m", "p");
        var b = new LlmResponse("x", 1, 2, 0.01m, t, "m", "p");

        // Act & Assert
        a.ShouldBe(b);
    }
}
