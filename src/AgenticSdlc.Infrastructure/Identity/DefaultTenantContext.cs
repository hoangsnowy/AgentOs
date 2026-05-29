// AgenticSdlc.Infrastructure/Identity/DefaultTenantContext.cs
// Single-operator fallback ITenantContext (Auth:Mode=operator). Everything maps to the "default" tenant
// with the admin role, so pre-multi-tenant behavior is preserved. Replaced by a claims-based context
// (read from the Keycloak token) when Auth:Mode=keycloak.

using System.Collections.Generic;
using AgenticSdlc.Application.Identity;

namespace AgenticSdlc.Infrastructure.Identity;

/// <summary>Default single-tenant context — tenant <c>default</c>, admin role, operator user.</summary>
public sealed class DefaultTenantContext : ITenantContext
{
    /// <inheritdoc />
    public string TenantId => ITenantContext.DefaultTenantId;

    /// <inheritdoc />
    public string? UserId => "operator";

    /// <inheritdoc />
    public string? UserName => "operator";

    /// <inheritdoc />
    public IReadOnlyList<string> Roles { get; } = new[] { "admin" };

    /// <inheritdoc />
    public bool IsAuthenticated => false;

    /// <inheritdoc />
    public bool IsAdmin => true;
}
