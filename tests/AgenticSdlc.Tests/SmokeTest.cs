// Smoke test xác nhận test infrastructure (xUnit v3 + Shouldly + NSubstitute) build được.
// Sẽ được thay bằng test thật ở Phase 5.

using Shouldly;
using Xunit;

namespace AgenticSdlc.Tests;

public class SmokeTest
{
    [Fact]
    public void TestRunner_Should_Be_Wired_Up()
    {
        // Arrange
        const string expected = "agentic-sdlc-net";

        // Act
        var actual = "agentic-sdlc-net";

        // Assert
        actual.ShouldBe(expected);
    }
}
