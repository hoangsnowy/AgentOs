// Claims-based ITenantContext for Keycloak OIDC — reads the tenant + user + roles from the
// validated OIDC token on the current request (HttpContext.User). Registered for authenticated
// requests; anonymous requests fall back to DefaultTenantContext.

using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using AgentOs.SharedKernel.Identity;
using Microsoft.AspNetCore.Http;

namespace AgentOs.Modules.Identity;

/// <summary>Resolves the tenant context from the current request's authenticated principal.</summary>
public sealed class HttpTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _accessor;

    public HttpTenantContext(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? User => _accessor.HttpContext?.User;

    /// <inheritdoc />
    /// <remarks>
    /// Fail-closed for a real signed-in principal: an authenticated Keycloak user is always issued a
    /// <c>tenant</c> claim by the realm, so a missing claim means a misconfigured token, NOT "use the
    /// shared default tenant". Returning <see cref="ITenantContext.DefaultTenantId"/> there would let
    /// such a token read/write the <c>default</c> tenant's data (cross-tenant leak). Instead return
    /// empty — tenant-scoped reads then match no rows and writes throw (<c>SetForTenantAsync</c> rejects
    /// empty). The default is kept ONLY for the unauthenticated/anonymous case.
    /// </remarks>
    public string TenantId
    {
        get
        {
            var user = User;
            if (user?.FindFirst("tenant")?.Value is { Length: > 0 } tenant) { return tenant; }
            return user?.Identity?.IsAuthenticated == true ? string.Empty : ITenantContext.DefaultTenantId;
        }
    }

    /// <inheritdoc />
    public string? UserId => User?.FindFirst("sub")?.Value;

    /// <inheritdoc />
    public string? UserName => User?.FindFirst("preferred_username")?.Value ?? User?.Identity?.Name;

    /// <inheritdoc />
    public IReadOnlyList<string> Roles =>
        User?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList() ?? new List<string>();

    /// <inheritdoc />
    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    /// <inheritdoc />
    public bool IsAdmin => Roles.Contains("admin");
}
