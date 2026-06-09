// Coherence Phase 2 (A2a) — the greenfield router is the safety gate that keeps the Quality engine (the
// 5-agent pipeline, which generates NEW files) from ever running an edit/bug ticket. The headline
// assertion (red-team HIGH): a type:bug ticket classifies as Edit, NEVER Greenfield.

using AgentOs.Modules.Pipeline.Sessions;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Pipeline;

public sealed class TicketClassifierTests
{
    [Theory]
    [InlineData("type:feature")]
    [InlineData("TYPE:FEATURE")]
    [InlineData("  type:feature  ")]
    public void Classify_TypeFeatureLabel_IsGreenfield(string type) =>
        TicketClassifier.Classify(type, "anything").ShouldBe(TicketClass.Greenfield);

    [Theory]
    [InlineData("type:bug")]
    [InlineData("type:chore")]
    [InlineData("type:spike")]
    public void Classify_NonFeatureLabel_IsEdit_NeverGreenfield(string type)
    {
        // A greenfield-sounding title must NOT override an explicit non-feature label.
        var cls = TicketClassifier.Classify(type, "Add a shiny new feature");
        cls.ShouldBe(TicketClass.Edit);
        cls.ShouldNotBe(TicketClass.Greenfield);
    }

    [Fact]
    public void Classify_NoLabel_FeatureTitle_IsGreenfield() =>
        TicketClassifier.Classify(null, "Add CSV export to the dashboard").ShouldBe(TicketClass.Greenfield);

    [Fact]
    public void Classify_NoLabel_FixTitle_IsEdit() =>
        TicketClassifier.Classify(null, "Fix crash when saving an empty form").ShouldBe(TicketClass.Edit);

    [Fact]
    public void Classify_NoLabel_EditVerbWinsOverGreenfieldVerb() =>
        // "fix" (edit) wins over "add" (greenfield) — a negative signal is stronger.
        TicketClassifier.Classify(null, "Add a guard to fix the null-ref").ShouldBe(TicketClass.Edit);

    [Theory]
    [InlineData("Dashboard")]
    [InlineData("")]
    [InlineData(null)]
    public void Classify_NoLabel_VagueOrEmptyTitle_IsAmbiguous(string? title) =>
        TicketClassifier.Classify(null, title).ShouldBe(TicketClass.Ambiguous);

    [Fact]
    public void RefusalMessage_Edit_MentionsGreenfield() =>
        TicketClassifier.RefusalMessage(TicketClass.Edit).ShouldContain("greenfield");

    [Fact]
    public void RefusalMessage_Greenfield_IsEmpty() =>
        TicketClassifier.RefusalMessage(TicketClass.Greenfield).ShouldBeEmpty();
}
