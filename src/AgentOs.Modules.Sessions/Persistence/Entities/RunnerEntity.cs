// M3 — a runner = a member's paired dev machine (schema sessions.runners). Long-lived ("standing"):
// paired once, then reused across that member's sessions. Stores only the salted token HASH, never the
// plaintext pairing token. Tenant-stamped on write; OwnerUserId binds it to the member whose work it runs.

using System;

namespace AgentOs.Modules.Sessions.Persistence.Entities;

/// <summary>A persisted runner = a paired dev machine bound to one member.</summary>
public sealed class RunnerEntity
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;

    /// <summary>The member (token <c>sub</c>) this machine belongs to; dispatch targets the member's runner.</summary>
    public string OwnerUserId { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    /// <summary>Salted hash of the pairing token (format <c>sha256$salt$hash</c>). Never the plaintext.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Pending → Paired (first successful connect) → Revoked.</summary>
    public string Status { get; set; } = "Pending";

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? LastSeenUtc { get; set; }
    public string? CreatedByUserId { get; set; }
}
