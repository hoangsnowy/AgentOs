// Batch 1 — boundary validation for the pipeline endpoints: a malformed body must become a 400
// ValidationProblem field map at the API edge, never an ArgumentException inside an agent.
using AgentOs.Domain;
using AgentOs.Domain.Code;
using AgentOs.Domain.Pipeline;
using AgentOs.Domain.Requirements;
using AgentOs.Domain.Testing;
using AgentOs.Modules.Pipeline.Endpoints;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Endpoints;

public class PipelineRequestValidationTests
{
    private static AgentMetrics Metrics => new("Anthropic", "claude-sonnet-4-6", 1, 1, 0m, TimeSpan.FromSeconds(1));
    private static RequirementSpec Spec => new("T", "Desc", [], [], [], [], [], [], Metrics);
    private static CodeArtifact Code => new("Proj", "Clean", [], null, Metrics);
    private static TestArtifact Tests => new("xUnit", [], 1, 1, 1, 80, Metrics);

    // ── UserStory (/requirement, /pipeline, /pipeline/stream) ───────────────────────────────

    [Fact]
    public void ForStory_ValidBody_ReturnsNull() =>
        PipelineRequestValidation.ForStory(new UserStory("As a user, I want X.")).ShouldBeNull();

    [Fact]
    public void ForStory_NullBody_ReturnsBodyRequiredError()
    {
        var errors = PipelineRequestValidation.ForStory(null);
        errors.ShouldNotBeNull();
        errors.ShouldContainKey("");
    }

    [Fact]
    public void ForStory_WhitespaceDescription_ReturnsDescriptionError()
    {
        var errors = PipelineRequestValidation.ForStory(new UserStory("   "));
        errors.ShouldNotBeNull();
        errors.ShouldContainKey("description");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    public void ForStory_NMaxOutOfRange_ReturnsNMaxError(int nMax)
    {
        var errors = PipelineRequestValidation.ForStory(new UserStory("Story", nMax));
        errors.ShouldNotBeNull();
        errors.ShouldContainKey("nMax");
    }

    [Fact]
    public void ForStory_WhitespaceLocale_ReturnsLocaleError()
    {
        var errors = PipelineRequestValidation.ForStory(new UserStory("Story", Locale: " "));
        errors.ShouldNotBeNull();
        errors.ShouldContainKey("locale");
    }

    // ── CodeRequest (/code) ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ForCode_ValidBody_ReturnsNull() =>
        PipelineRequestValidation.ForCode(new CodeRequest(Spec)).ShouldBeNull();

    [Fact]
    public void ForCode_NullSpec_ReturnsSpecError()
    {
        var errors = PipelineRequestValidation.ForCode(new CodeRequest(null!));
        errors.ShouldNotBeNull();
        errors.ShouldContainKey("spec");
    }

    // ── TestRequest (/test) ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ForTest_ValidBody_ReturnsNull() =>
        PipelineRequestValidation.ForTest(new TestRequest(Spec, Code)).ShouldBeNull();

    [Fact]
    public void ForTest_MissingCode_ReturnsCodeError()
    {
        var errors = PipelineRequestValidation.ForTest(new TestRequest(Spec, null!));
        errors.ShouldNotBeNull();
        errors.ShouldContainKey("code");
    }

    // ── QaRequest (/qa) ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void ForQa_ValidBody_ReturnsNull() =>
        PipelineRequestValidation.ForQa(new QaRequest(Spec, Code, Tests)).ShouldBeNull();

    [Fact]
    public void ForQa_MissingEverything_ReportsAllFields()
    {
        var errors = PipelineRequestValidation.ForQa(new QaRequest(null!, null!, null!));
        errors.ShouldNotBeNull();
        errors.ShouldContainKey("spec");
        errors.ShouldContainKey("code");
        errors.ShouldContainKey("tests");
    }

    [Fact]
    public void ForQa_NullBody_ReturnsBodyRequiredError()
    {
        var errors = PipelineRequestValidation.ForQa(null);
        errors.ShouldNotBeNull();
        errors.ShouldContainKey("");
    }
}
