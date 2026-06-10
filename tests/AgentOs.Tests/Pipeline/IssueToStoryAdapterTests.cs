// Coherence Phase 2 (A2a) — the adapter that turns a Spine ticket into the pipeline's UserStory input.

using AgentOs.Modules.Pipeline.Sessions;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Pipeline;

public sealed class IssueToStoryAdapterTests
{
    [Fact]
    public void ToUserStory_TitleAndBody_ComposesDescription()
    {
        var story = IssueToStoryAdapter.ToUserStory("Add export", "As a user I want CSV", 5, "en-US");
        story.Description.ShouldBe("Add export\n\nAs a user I want CSV");
        story.NMax.ShouldBe(5);
        story.Locale.ShouldBe("en-US");
    }

    [Fact]
    public void ToUserStory_NoBody_DescriptionIsTitleOnly() =>
        IssueToStoryAdapter.ToUserStory("Add export", null, 3).Description.ShouldBe("Add export");

    [Fact]
    public void ToUserStory_WhitespaceBody_DescriptionIsTitleOnly() =>
        IssueToStoryAdapter.ToUserStory("Add export", "   ", 3).Description.ShouldBe("Add export");

    [Fact]
    public void ToUserStory_DefaultLocale_IsEnUs() =>
        IssueToStoryAdapter.ToUserStory("Add export", null, 3).Locale.ShouldBe("en-US");
}
