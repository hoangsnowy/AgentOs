// Bootstrap (slice 2) — the deterministic RequirementSpec → seed-tickets mapping.

using System.Linq;
using AgentOs.Domain.Workspaces;
using AgentOs.Modules.Pipeline.Decomposition;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Decomposition;

public sealed class TicketMapperTests
{
    [Fact]
    public void Map_ProducesEpicPlusOneTicketPerRequirement()
    {
        var spec = DecompositionFixtures.Spec(
            functionalRequirements: ["Admin creates a product", "Customer searches products"]);

        var drafts = TicketMapper.Map(spec);

        drafts.Count.ShouldBe(3); // 1 epic + 2 requirements

        var epic = drafts[0];
        epic.AiReady.ShouldBeFalse();
        epic.Labels.ShouldContain(StandardLabels.NeedsHuman);

        var children = drafts.Skip(1).ToList();
        children.ShouldAllBe(d => d.AiReady);
        children.ShouldAllBe(d => d.Labels.Contains(StandardLabels.AiReady));
        children.ShouldAllBe(d => d.Labels.Contains("type:feature"));
    }

    [Fact]
    public void Map_DerivesAreaLabelFromText()
    {
        var spec = DecompositionFixtures.Spec(
            functionalRequirements: ["Expose a GET endpoint that lists products", "Render the product list page UI"],
            endpoints: true);

        var drafts = TicketMapper.Map(spec);

        drafts[1].Labels.ShouldContain("area:api");
        drafts[2].Labels.ShouldContain("area:ui");
    }

    [Fact]
    public void Map_RequirementBody_HasAcceptanceChecklist()
    {
        var spec = DecompositionFixtures.Spec(
            functionalRequirements: ["Do the thing"],
            acceptanceCriteria: ["First criterion", "Second criterion"]);

        var drafts = TicketMapper.Map(spec);

        drafts[1].Body.ShouldContain("## Acceptance criteria");
        drafts[1].Body.ShouldContain("- [ ] First criterion");
        drafts[1].Body.ShouldContain("- [ ] Second criterion");
    }

    [Fact]
    public void Map_LongRequirement_TitleTruncatedTo80()
    {
        var longReq = new string('x', 200);
        var spec = DecompositionFixtures.Spec(functionalRequirements: [longReq]);

        var drafts = TicketMapper.Map(spec);

        drafts[1].Title.Length.ShouldBeLessThanOrEqualTo(80);
    }
}
