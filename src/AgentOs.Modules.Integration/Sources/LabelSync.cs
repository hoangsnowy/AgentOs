// Bootstrap — the pure diff behind an idempotent label seed, split out from the Octokit call so it
// can be unit-tested without a live GitHub. GitHub label names are case-insensitive, so existing
// labels are matched case-insensitively against the desired set.

using System;
using System.Collections.Generic;
using AgentOs.Domain.Workspaces;

namespace AgentOs.Modules.Integration.Sources;

/// <summary>Pure helpers for reconciling a desired label set against what a repo already has.</summary>
internal static class LabelSync
{
    /// <summary>
    /// Split <paramref name="desired"/> into the labels missing from <paramref name="existingNames"/>
    /// (to create) and those already present (to skip), matched by name case-insensitively.
    /// </summary>
    internal static (IReadOnlyList<LabelSpec> ToCreate, IReadOnlyList<string> Existing) Partition(
        IEnumerable<string> existingNames, IReadOnlyList<LabelSpec> desired)
    {
        ArgumentNullException.ThrowIfNull(existingNames);
        ArgumentNullException.ThrowIfNull(desired);

        var have = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        var toCreate = new List<LabelSpec>();
        var existing = new List<string>();
        foreach (var spec in desired)
        {
            if (have.Contains(spec.Name))
            {
                existing.Add(spec.Name);
            }
            else
            {
                toCreate.Add(spec);
            }
        }
        return (toCreate, existing);
    }

    /// <summary>Strip a leading <c>#</c> from a hex color — GitHub's NewLabel wants the bare 6 hex chars.</summary>
    internal static string NormalizeColor(string color)
        => string.IsNullOrEmpty(color) ? color : color.TrimStart('#');
}
