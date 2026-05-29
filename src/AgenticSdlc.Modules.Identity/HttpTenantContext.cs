// Claims-based ITenantContext for Auth:Mode=keycloak — reads the tenant + user + roles from the
// validated OIDC token on the current request (HttpContext.User). Registered only in keycloak mode;
// operator mode keeps DefaultTenantContext.

using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using AgenticSdlc.SharedKernel.Identity;
using Microsoft.AspNetCore.Http;

namespace AgenticSdlc.Modules.Identity;

/// <summary>Resolves the tenant context from the current request's authenticated principal.</summary>
public sealed class HttpTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _accessor;

    public HttpTenantContext(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? User => _accessor.HttpContext?.User;

    /// <inheritdoc />
    public string TenantId =>
        User?.FindFirst("tenant")?.Value is { Length: > 0 } tenant ? tenant : ITenantContext.DefaultTenantId;

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
