// Bootstrap (slice 2) — shared builders for the decomposition tests: a RequirementSpec and a
// deterministic ticket seed, parameterized just enough for the cases that matter.

using System;
using System.Collections.Generic;
using AgentOs.Domain;
using AgentOs.Domain.Requirements;
using AgentOs.Domain.Workspaces;

namespace AgentOs.Tests.Decomposition;

internal static class DecompositionFixtures
{
    public static RequirementSpec Spec(
        IReadOnlyList<string>? functionalRequirements = null,
        IReadOnlyList<string>? acceptanceCriteria = null,
        bool endpoints = false,
        bool entities = false)
        => new(
            Title: "Product management",
            Summary: "Admin can CRUD products.",
            Stakeholders: ["admin"],
            FunctionalRequirements: functionalRequirements ?? ["Admin creates a product"],
            NonFunctionalRequirements: [],
            Entities: entities ? [new EntityDescriptor("Product", ["id: Guid"], null)] : [],
            Endpoints: endpoints ? [new EndpointDescriptor("POST", "/products", "create a product", true)] : [],
            AcceptanceCriteria: acceptanceCriteria ?? ["SKU is unique"],
            Metrics: new AgentMetrics("Test", "m", 1, 1, 0m, TimeSpan.Zero));

    public static IReadOnlyList<TicketDraft> Seed()
        => [new TicketDraft("Seed epic", "body", ["type:feature", "area:core", "p1", StandardLabels.NeedsHuman], false)];
}
