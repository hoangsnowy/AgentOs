// Bootstrap — the write peer of the read-only board models. Seeding a standard label taxonomy onto a
// repo is the first write member of ISourceProvider. A LabelSpec is provider-neutral (GitHub label,
// later an Azure DevOps tag); LabelSyncResult reports what an idempotent seed actually changed so the
// UI can say "N created, M already present" without re-reading the repo.

using System.Collections.Generic;

namespace AgentOs.Domain.Workspaces;

/// <summary>A label to ensure exists on a repo: a name, a 6-hex color (no leading <c>#</c>), and an optional description.</summary>
/// <param name="Name">Label name (e.g. <c>type:feature</c>). Matched case-insensitively against existing labels.</param>
/// <param name="Color">6-char hex color without a leading <c>#</c> (e.g. <c>1f883d</c>).</param>
/// <param name="Description">Optional human description shown in the GitHub label UI.</param>
public sealed record LabelSpec(string Name, string Color, string? Description = null);

/// <summary>Outcome of an idempotent label seed: which labels were newly created vs already present.</summary>
/// <param name="Created">Names of labels created by this call.</param>
/// <param name="Existing">Names of requested labels that were already present (skipped).</param>
public sealed record LabelSyncResult(IReadOnlyList<string> Created, IReadOnlyList<string> Existing);
