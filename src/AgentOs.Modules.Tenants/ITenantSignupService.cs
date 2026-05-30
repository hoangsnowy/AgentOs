// Self-service signup orchestrator. Dispatches one of three modes based on the request shape:
//   - Invite: caller supplies an opaque, signed invitation token; joins an existing tenant.
//   - Slug: caller supplies an explicit tenant id; first user becomes the tenant Admin.
//   - AutoCreate: no slug, no invite; service generates a unique slug and the user is Admin.
// All three modes provision the Keycloak user FIRST and then write the registry row, with a
// Keycloak rollback if the registry write throws. Invitations are stateless DataProtection-
// signed payloads (no DB table) — they cannot be revoked before TTL but otherwise behave like a
// claim check.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace AgentOs.Modules.Tenants;

/// <summary>Self-service signup orchestrator. See file header for the dispatch contract.</summary>
public interface ITenantSignupService
{
    /// <summary>Provision the Keycloak user + (when needed) the tenant row, picking the mode from
    /// the request shape. Throws <see cref="InvalidOperationException"/> with a user-readable
    /// message on validation / invitation / collision errors.</summary>
    Task<TenantSignupOutcome> SignupAsync(TenantSignupRequest request, CancellationToken ct = default);

    /// <summary>Mint a signed, time-limited invitation token for the given tenant + role.</summary>
    InvitationToken CreateInvitation(string tenantId, string role, string? email, TimeSpan ttl);

    /// <summary>Decode an invitation token; returns null when the token is missing, malformed,
    /// or expired. Never throws — callers can branch on null to fall back to a different mode.</summary>
    InvitationPreview? PreviewInvitation(string? token);
}

/// <summary>Inputs for a single self-service signup attempt.</summary>
public sealed record TenantSignupRequest(
    string Username,
    string Password,
    string? Email,
    string? TenantId,
    string? TenantName,
    string? InviteToken);

/// <summary>Result of a successful signup.</summary>
public sealed record TenantSignupOutcome(string TenantId, string KeycloakUserId, SignupMode Mode);

/// <summary>Encrypted invitation payload + the URL the inviter sends out.</summary>
public sealed record InvitationToken(string Token, DateTimeOffset ExpiresAtUtc);

/// <summary>Decoded invitation payload. The caller may pre-fill a signup form from these values.</summary>
public sealed record InvitationPreview(string TenantId, string Role, string? Email, DateTimeOffset ExpiresAtUtc);

/// <summary>Which branch of <see cref="ITenantSignupService.SignupAsync"/> ran.</summary>
public enum SignupMode
{
    /// <summary>An invitation token resolved the tenant + role.</summary>
    Invite = 0,
    /// <summary>Caller supplied an explicit slug; tenant was created and caller became Admin.</summary>
    Slug = 1,
    /// <summary>Caller supplied no slug; a fresh slug was generated and caller became Admin.</summary>
    AutoCreate = 2,
}
