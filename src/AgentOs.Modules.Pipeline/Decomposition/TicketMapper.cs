// Bootstrap (slice 2) — the deterministic first pass of RequirementSpec → tickets. It produces a
// predictable seed: one umbrella epic (needs-human) plus one ticket per functional requirement
// (ai:ready), each with an area label derived from the text. The LLM right-size pass
// (TicketDecomposerAgent) then merges/splits/renames — but this guarantees a sane, testable baseline
// even if the LLM is unavailable.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AgentOs.Domain.Requirements;
using AgentOs.Domain.Workspaces;

namespace AgentOs.Modules.Pipeline.Decomposition;

/// <summary>Maps a <see cref="RequirementSpec"/> to a deterministic seed list of <see cref="TicketDraft"/>.</summary>
public static class TicketMapper
{
    /// <summary>Epic (umbrella, needs-human) + one ai:ready ticket per functional requirement.</summary>
    public static IReadOnlyList<TicketDraft> Map(RequirementSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var drafts = new List<TicketDraft>
        {
            // Epic — the umbrella. Not auto-run: it coordinates the child tickets.
            new(
                Title: Shorten(spec.Title, 80),
                Body: BuildEpicBody(spec),
                Labels: ["type:feature", DeriveArea($"{spec.Title} {spec.Summary}", spec), "p1", StandardLabels.NeedsHuman],
                AiReady: false),
        };

        // One buildable ticket per functional requirement.
        foreach (var requirement in spec.FunctionalRequirements)
        {
            drafts.Add(new TicketDraft(
                Title: Shorten(requirement, 80),
                Body: BuildRequirementBody(requirement, spec),
                Labels: ["type:feature", DeriveArea(requirement, spec), "p1", StandardLabels.AiReady],
                AiReady: true));
        }

        return drafts;
    }

    private static string BuildEpicBody(RequirementSpec spec)
    {
        var sb = new StringBuilder();
        sb.AppendLine(spec.Summary).AppendLine();

        if (spec.FunctionalRequirements.Count > 0)
        {
            sb.AppendLine("## Scope");
            foreach (var fr in spec.FunctionalRequirements)
            {
                sb.Append("- [ ] ").AppendLine(fr);
            }
            sb.AppendLine();
        }

        AppendAcceptanceCriteria(sb, spec.AcceptanceCriteria);
        AppendContext(sb, spec);
        return sb.ToString().TrimEnd();
    }

    private static string BuildRequirementBody(string requirement, RequirementSpec spec)
    {
        var sb = new StringBuilder();
        sb.AppendLine(requirement).AppendLine();
        AppendAcceptanceCriteria(sb, spec.AcceptanceCriteria);
        AppendContext(sb, spec);
        return sb.ToString().TrimEnd();
    }

    private static void AppendAcceptanceCriteria(StringBuilder sb, IReadOnlyList<string> criteria)
    {
        if (criteria.Count == 0)
        {
            return;
        }
        sb.AppendLine("## Acceptance criteria");
        foreach (var c in criteria)
        {
            sb.Append("- [ ] ").AppendLine(c);
        }
        sb.AppendLine();
    }

    private static void AppendContext(StringBuilder sb, RequirementSpec spec)
    {
        if (spec.Entities.Count > 0)
        {
            sb.AppendLine("## Entities");
            foreach (var e in spec.Entities)
            {
                sb.Append("- **").Append(e.Name).Append("** — ").AppendLine(string.Join(", ", e.Fields));
            }
            sb.AppendLine();
        }
        if (spec.Endpoints.Count > 0)
        {
            sb.AppendLine("## Endpoints");
            foreach (var ep in spec.Endpoints)
            {
                sb.Append("- `").Append(ep.Method).Append(' ').Append(ep.Path).Append("` — ").AppendLine(ep.Purpose);
            }
            sb.AppendLine();
        }
    }

    // Pick an area label from the text, falling back to the spec shape (endpoints → api, entities → data).
    private static string DeriveArea(string text, RequirementSpec spec)
    {
        var t = text.ToLowerInvariant();
        if (ContainsAny(t, "ui", "page", "screen", "button", "form", "view", "frontend", "layout", "css"))
        {
            return "area:ui";
        }
        if (ContainsAny(t, "endpoint", "api", "route", "http", "rest", "request", "response"))
        {
            return "area:api";
        }
        if (ContainsAny(t, "entity", "model", "table", "database", "persist", "store", "migration", "schema", "repository"))
        {
            return "area:data";
        }
        if (ContainsAny(t, "deploy", "docker", "infra", "config", "build", "ci/cd", "pipeline"))
        {
            return "area:infra";
        }
        if (spec.Endpoints.Count > 0)
        {
            return "area:api";
        }
        if (spec.Entities.Count > 0)
        {
            return "area:data";
        }
        return "area:core";
    }

    private static bool ContainsAny(string haystack, params string[] needles)
        => needles.Any(n => haystack.Contains(n, StringComparison.Ordinal));

    // Trim to a crisp one-liner: first sentence if it fits, else a truncated single line with an ellipsis.
    private static string Shorten(string s, int max)
    {
        s = s.Trim();
        var firstLine = s.Split('\n', '.')[0].Trim();
        var pick = firstLine.Length is > 0 and <= 80 ? firstLine : s;
        if (pick.Length > max)
        {
            pick = string.Concat(pick.AsSpan(0, max - 1).TrimEnd(), "…");
        }
        return pick;
    }
}
