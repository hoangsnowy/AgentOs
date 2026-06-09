// Coherence Phase 2 (A2b) — the Quick/Quality engine selection rule: Quality (server-side pipeline) and
// "run on my machine" (runner CLI) are mutually exclusive.

using AgentOs.Modules.Pipeline.Sessions;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Pipeline;

public sealed class EngineSelectionTests
{
    [Fact]
    public void Validate_QualityWithRunOnMachine_IsRejected() =>
        EngineSelection.Validate(EngineSelection.Quality, runOnMachine: true).ShouldNotBeNull();

    [Fact]
    public void Validate_QualityWithoutRunOnMachine_IsValid() =>
        EngineSelection.Validate(EngineSelection.Quality, runOnMachine: false).ShouldBeNull();

    [Fact]
    public void Validate_QuickWithRunOnMachine_IsValid() =>
        EngineSelection.Validate(EngineSelection.Quick, runOnMachine: true).ShouldBeNull();

    [Theory]
    [InlineData("Quality")]
    [InlineData("quality")]
    [InlineData("  Quality  ")]
    public void IsQuality_True_CaseAndWhitespaceTolerant(string brain) =>
        EngineSelection.IsQuality(brain.Trim()).ShouldBeTrue();

    [Theory]
    [InlineData("Quick")]
    [InlineData(null)]
    [InlineData("")]
    public void IsQuality_NonQuality_IsFalse(string? brain) =>
        EngineSelection.IsQuality(brain).ShouldBeFalse();
}
