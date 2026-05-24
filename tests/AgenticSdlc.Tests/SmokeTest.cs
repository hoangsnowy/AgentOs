// Smoke test confirming the test infrastructure (xUnit v3 + Shouldly + NSubstitute) builds.
// To be replaced by real tests in Phase 5.

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
