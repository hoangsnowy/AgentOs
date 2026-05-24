// AgenticSdlc.Domain/Requirements/RequirementSpec.cs
// Phase 3 — Output of RequirementAgent — structured requirement specification.

using System.Collections.Generic;

namespace AgenticSdlc.Domain.Requirements;

/// <summary>
/// Structured requirement specification — output of <c>IRequirementAgent</c>.
/// JSON-serializable, to be passed to CodingAgent + TestingAgent + QaAgent.
/// </summary>
/// <param name="Title">Short feature title.</param>
/// <param name="Summary">1-2 sentence summary.</param>
/// <param name="Stakeholders">Relevant roles (e.g. "admin", "walk-in customer").</param>
/// <param name="FunctionalRequirements">Functional requirements — e.g. "Admin creates a product with a unique SKU".</param>
/// <param name="NonFunctionalRequirements">Non-functional requirements — e.g. "API response p95 ≤ 200ms".</param>
/// <param name="Entities">Domain entities — driver for CodingAgent to generate models.</param>
/// <param name="Endpoints">API endpoints — the surface to expose.</param>
/// <param name="AcceptanceCriteria">Acceptance criteria — driver for TestingAgent.</param>
/// <param name="Metrics">Agent metric (token, cost, latency).</param>
public sealed record RequirementSpec(
    string Title,
    string Summary,
    IReadOnlyList<string> Stakeholders,
    IReadOnlyList<string> FunctionalRequirements,
    IReadOnlyList<string> NonFunctionalRequirements,
    IReadOnlyList<EntityDescriptor> Entities,
    IReadOnlyList<EndpointDescriptor> Endpoints,
    IReadOnlyList<string> AcceptanceCriteria,
    AgentMetrics Metrics);

/// <summary>Domain entity descriptor — passed directly to CodingAgent to generate a class/record.</summary>
/// <param name="Name">Entity name (PascalCase).</param>
/// <param name="Fields">List of fields <c>{Name}: {Type}</c>.</param>
/// <param name="Notes">Additional notes (constraints, invariants).</param>
public sealed record EntityDescriptor(string Name, IReadOnlyList<string> Fields, string? Notes = null);

/// <summary>API endpoint descriptor.</summary>
/// <param name="Method">HTTP method (<c>GET</c>, <c>POST</c>, ...).</param>
/// <param name="Path">Route template (e.g. <c>/products/{id}</c>).</param>
/// <param name="Purpose">Description of the purpose.</param>
/// <param name="AuthRequired">Whether authentication is required.</param>
public sealed record EndpointDescriptor(string Method, string Path, string Purpose, bool AuthRequired = false);
