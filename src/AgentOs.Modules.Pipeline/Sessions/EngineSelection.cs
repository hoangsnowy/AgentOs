// Coherence Phase 2 (A2b) — the engine a Spine session runs under and the rule that keeps the choice
// coherent. "Quick" = IssueWorkAgent (an edit loop dispatched to the paired runner). "Quality" = the
// 5-agent pipeline (server-side). Pure so the mutual-exclusion guard is unit-tested without a circuit.

using System;

namespace AgentOs.Modules.Pipeline.Sessions;

/// <summary>The two Spine engines + the validity rule for selecting one.</summary>
public static class EngineSelection
{
    /// <summary>The runner edit loop (IssueWorkAgent) — the default.</summary>
    public const string Quick = "Quick";

    /// <summary>The 5-agent SDLC pipeline (server-side, greenfield only).</summary>
    public const string Quality = "Quality";

    /// <summary>True when <paramref name="brain"/> selects the Quality engine (case-insensitive).</summary>
    public static bool IsQuality(string? brain) =>
        string.Equals(brain, Quality, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Null when the selection is valid; an error message when Quality is paired with "run on my machine".
    /// The pipeline runs server-side and run-on-machine routes to the runner CLI, so the two are mutually
    /// exclusive.
    /// </summary>
    public static string? Validate(string brain, bool runOnMachine) =>
        IsQuality(brain) && runOnMachine
            ? "Quality runs the 5-agent pipeline server-side — turn off \"Run on my machine\" to use it."
            : null;
}
