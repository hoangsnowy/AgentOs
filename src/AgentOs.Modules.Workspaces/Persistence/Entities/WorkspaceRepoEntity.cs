// Board reshape — a repo (service) under a board (WorkspaceEntity). A board spans many of these; a
// ticket on the board maps to one of them, and a multi-repo session (later stage) targets several.
// Tenant-stamped + tenant-filtered, same convention as the parent.

using System;

namespace AgentOs.Modules.Workspaces.Persistence.Entities;

/// <summary>A repository connected under a board (<see cref="WorkspaceEntity"/>).</summary>
public sealed class WorkspaceRepoEntity
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;

    /// <summary>The owning board (<see cref="WorkspaceEntity.Id"/>).</summary>
    public Guid WorkspaceId { get; set; }

    public string Owner { get; set; } = string.Empty;
    public string Repo { get; set; } = string.Empty;
    public string DefaultBranch { get; set; } = "main";
    public string RemoteUrl { get; set; } = string.Empty;
    public bool Private { get; set; }
    public DateTimeOffset AddedAtUtc { get; set; }
}
