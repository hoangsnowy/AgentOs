// AgenticSdlc.Api/Endpoints/TenantEndpoints.cs
// /tenants/me — returns the resolved ITenantContext for the current request so the Web UI can
// render the current tenant id, user, and roles without re-parsing the OIDC token client-side.
// Phase C adds POST /tenants (admin creates a tenant + admin user) and POST /tenants/{id}/members.

using AgenticSdlc.Application.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace AgenticSdlc.Api.Endpoints;

/// <summary>Maps the tenant identity endpoints.</summary>
public static class TenantEndpoints
{
    /// <summary>Mount the endpoints onto <paramref name="app"/>.</summary>
    public static WebApplication MapTenantEndpoints(this WebApplication app)
    {
        System.ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/tenants/me", (ITenantContext tenant) => Results.Ok(new TenantMeResponse(
            tenant.TenantId,
            tenant.UserId,
            tenant.UserName,
            tenant.Roles,
            tenant.IsAuthenticated,
            tenant.IsAdmin)))
        .WithName("TenantMe")
        .WithSummary("Resolved tenant + user + roles for the current request")
        .WithTags("Tenants")
        .RequireAuthorization();

        return app;
    }
}

/// <summary>Response shape for GET /tenants/me.</summary>
public sealed record TenantMeResponse(
    string TenantId,
    string? UserId,
    string? UserName,
    System.Collections.Generic.IReadOnlyList<string> Roles,
    bool IsAuthenticated,
    bool IsAdmin);
