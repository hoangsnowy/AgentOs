// M3 — a remote session = one unit of work, a member × workspace (schema sessions.sessions). It is the
// durable record of "member M wants to act on workspace W"; live execution is dispatched to that
// member's runner. Tenant-stamped on write; reads are tenant-filtered by the DbContext query filter.

using System;

namespace AgentOs.Modules.Sessions.Persistence.Entities;

/// <summary>A persisted remote session = a member's unit of work against one connected workspace.</summary>
public sealed class RemoteSessionEntity
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;

    /// <summary>The connected workspace (Workspaces module) this session acts on.</summary>
    public Guid WorkspaceId { get; set; }

    /// <summary>The member (token <c>sub</c>) who owns the session; their runner executes it.</summary>
    public string MemberUserId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>Draft → Active → Closed (or Failed).</summary>
    public string Status { get; set; } = "Draft";

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ClosedAtUtc { get; set; }
    public string? CreatedByUserId { get; set; }

    // M5 — run result fields, populated after the issue-work agent completes.
    public int? IssueNumber { get; set; }
    public string? PrUrl { get; set; }
    public string? Error { get; set; }

    // Board reshape — the PRIMARY repo this session targets (the ticket's own repo). A session can now
    // fan out to N repos (SessionRepoEntity children); these stay as the primary for back-compat
    // display. Nullable for pre-board / migrated sessions.
    public string? RepoOwner { get; set; }
    public string? RepoName { get; set; }
    public string? RepoDefaultBranch { get; set; }

    // Multi-repo — the board item this session came from (for traceability) + its kind.
    public string? BoardItemNodeId { get; set; }
    public string? TicketKind { get; set; }
}
