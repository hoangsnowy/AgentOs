// Cross-cutting tenant + user identity for the current scope. Lives in SharedKernel because every
// module's persistence layer + key pool filter by it. Repositories and the LLM key pool read TenantId
// so one tenant never sees another's data or secrets. Until OIDC is wired (Auth:Mode=operator), a
// DefaultTenantContext returns the singleton "default" tenant so the app keeps working.

using System.Collections.Generic;

namespace AgenticSdlc.SharedKernel.Identity;

/// <summary>Tenant + user identity for the current scope.</summary>
public interface ITenantContext
{
    /// <summary>The tenant the current request belongs to. Never empty — single-operator mode uses
    /// <see cref="DefaultTenantId"/>.</summary>
    string TenantId { get; }

    /// <summary>The authenticated user id (token <c>sub</c>), or <c>null</c> when anonymous/operator.</summary>
    string? UserId { get; }

    /// <summary>Display name / username, or <c>null</c>.</summary>
    string? UserName { get; }

    /// <summary>Realm roles granted to the user (e.g. <c>admin</c>, <c>member</c>).</summary>
    IReadOnlyList<string> Roles { get; }

    /// <summary>True when a real authenticated user backs this context.</summary>
    bool IsAuthenticated { get; }

    /// <summary>True when the user holds the <c>admin</c> role in their tenant.</summary>
    bool IsAdmin { get; }

    /// <summary>The tenant id used in single-operator mode and for migrated pre-tenant data.</summary>
    public const string DefaultTenantId = "default";
}
