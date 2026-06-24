// Single source of truth for which graph AgentRole strings map to a runnable typed agent. Used by BOTH
// the planner (validation) and the executor (dispatch) so they can't drift. Lives in Domain alongside the
// graph DTOs because the planner (Domain) needs it for validation and the executor (Pipeline module) needs
// it for dispatch — so it is public, not internal.

using System;

namespace AgentOs.Domain.Pipeline.Graph;

public static class AgentRoleMap
{
    /// <summary>Canonical role name for a runnable agent, or null if the role isn't recognised.</summary>
    public static string? Canonical(string? role) => role?.Trim().ToUpperInvariant() switch
    {
        "REQUIREMENT" => "Requirement",
        "CODING" => "Coding",
        "TESTING" => "Testing",
        "QA" => "Qa",
        _ => null,
    };

    /// <summary>Whether the role maps to a runnable agent.</summary>
    public static bool IsKnown(string? role) => Canonical(role) is not null;
}
