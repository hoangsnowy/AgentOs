// Coherence Phase 2 (A2a) — the greenfield router (red-team HIGH). The Quality engine (the 5-agent
// pipeline) produces NEW files; it cannot edit an existing repo. So a bug-fix/edit ticket flowing into
// it would scaffold duplicate/garbage files. This classifier is the HARD gate: greenfield → pipeline,
// edit → refuse (the caller routes to Quick/IssueWork or surfaces a clear refusal), ambiguous → refuse.
// Never silent best-effort. Pure + deterministic so it is unit-tested directly (no LLM, no I/O).

using System;
using System.Linq;
using AgentOs.Domain.Workspaces;

namespace AgentOs.Modules.Pipeline.Sessions;

/// <summary>How a ticket maps to an execution engine for routing.</summary>
public enum TicketClass
{
    /// <summary>New capability / scaffold — safe for the greenfield Quality pipeline.</summary>
    Greenfield,

    /// <summary>A change to existing behaviour (bug, chore, spike) — needs the edit/Quick path.</summary>
    Edit,

    /// <summary>No reliable signal — refuse rather than guess.</summary>
    Ambiguous,
}

/// <summary>Classifies a ticket so the Quality engine only ever runs greenfield work.</summary>
public static class TicketClassifier
{
    // Title-verb fallback used only when the ticket carries no `type:*` label.
    private static readonly string[] GreenfieldVerbs =
        ["add", "create", "implement", "build", "scaffold", "introduce", "new "];

    private static readonly string[] EditVerbs =
        ["fix", "bug", "repair", "resolve", "defect", "broken", "crash", "error", "regression", "refactor"];

    /// <summary>
    /// Classify a ticket. <paramref name="ticketType"/> is the captured <c>type:*</c> label (the
    /// authoritative signal when present); <paramref name="title"/> is the keyword fallback.
    /// </summary>
    public static TicketClass Classify(string? ticketType, string? title)
    {
        var type = ticketType?.Trim().ToLowerInvariant();
        switch (type)
        {
            case "type:feature":
                return TicketClass.Greenfield;
            // bug = edit by definition; chore/spike are maintenance/investigation, not greenfield builds.
            case "type:bug":
            case "type:chore":
            case "type:spike":
                return TicketClass.Edit;
        }

        // No usable type label → fall back to the title verb. Edit signals win over greenfield ones
        // (a "fix" headline is a stronger negative than an "add" is a positive), then greenfield, else refuse.
        var t = title?.Trim().ToLowerInvariant() ?? string.Empty;
        if (t.Length == 0)
        {
            return TicketClass.Ambiguous;
        }
        if (EditVerbs.Any(v => t.Contains(v, StringComparison.Ordinal)))
        {
            return TicketClass.Edit;
        }
        if (GreenfieldVerbs.Any(v => t.StartsWith(v, StringComparison.Ordinal) || t.Contains(" " + v, StringComparison.Ordinal)))
        {
            return TicketClass.Greenfield;
        }
        return TicketClass.Ambiguous;
    }

    /// <summary>A user-facing reason the Quality engine refused this ticket.</summary>
    public static string RefusalMessage(TicketClass cls) => cls switch
    {
        TicketClass.Edit =>
            "Quality engine refused — it only runs greenfield feature tickets (it generates new files, it " +
            $"cannot edit an existing repo). This looks like an edit/bug ticket. Use the Quick engine instead, " +
            $"or label it {StandardLabels.AiReady} as type:feature.",
        TicketClass.Ambiguous =>
            "Quality engine refused — could not confirm this is a greenfield feature ticket (no type:feature " +
            "label and an ambiguous title). Label it type:feature to run Quality, or use the Quick engine.",
        _ => string.Empty,
    };
}
