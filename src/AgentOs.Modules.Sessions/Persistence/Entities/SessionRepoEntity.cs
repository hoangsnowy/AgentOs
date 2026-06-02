// Multi-repo — a repo a session targets. One session fans out to N of these; the agent edits each,
// and each gets its own branch + PR. The parent RemoteSessionEntity keeps a "primary" repo (the
// ticket's own repo) for back-compat display; the per-repo run results live here.

using System;

namespace AgentOs.Modules.Sessions.Persistence.Entities;

/// <summary>A repository targeted by a session, with its own branch / PR / status.</summary>
public sealed class SessionRepoEntity
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;

    /// <summary>The owning session (<see cref="RemoteSessionEntity.Id"/>).</summary>
    public Guid SessionId { get; set; }

    /// <summary>The curated board repo (workspaces.workspace_repos) this maps to, if any.</summary>
    public Guid? WorkspaceRepoId { get; set; }

    public string Owner { get; set; } = string.Empty;
    public string Repo { get; set; } = string.Empty;
    public string DefaultBranch { get; set; } = "main";

    /// <summary>Pending → Running → Done / Failed.</summary>
    public string Status { get; set; } = "Pending";

    public string? BranchName { get; set; }
    public string? PrUrl { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}
