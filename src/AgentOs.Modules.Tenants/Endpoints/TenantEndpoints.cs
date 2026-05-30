// Tenant identity + admin endpoints. /tenants/me is open to any authenticated user (returns the
// resolved context); /tenants list + create + member invite require the Admin policy and reach
// into Keycloak via IKeycloakAdminClient to provision realm users.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Modules.Tenants.Keycloak;
using AgentOs.SharedKernel.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AgentOs.Modules.Tenants.Endpoints;

internal static class TenantEndpoints
{
    public static IEndpointRouteBuilder MapTenantEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

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

        app.MapGet("/tenants", async (ITenantsRepository repo, CancellationToken ct) =>
        {
            var list = await repo.ListAsync(ct).ConfigureAwait(false);
            return Results.Ok(list);
        })
        .WithName("TenantsList")
        .WithSummary("List every tenant (admin only)")
        .WithTags("Tenants")
        .RequireAuthorization("Admin");

        app.MapPost("/tenants", async (
            CreateTenantRequest body,
            ITenantsRepository repo,
            IKeycloakAdminClient kc,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Id) || string.IsNullOrWhiteSpace(body.AdminUsername))
            {
                return Results.BadRequest(new { error = "id and adminUsername are required" });
            }

            var existing = await repo.GetAsync(body.Id, ct).ConfigureAwait(false);
            if (existing is not null)
            {
                return Results.Conflict(new { error = $"Tenant '{body.Id}' already exists" });
            }

            var record = new TenantRecord(body.Id, string.IsNullOrWhiteSpace(body.Name) ? body.Id : body.Name, DateTimeOffset.UtcNow);
            await repo.AddAsync(record, ct).ConfigureAwait(false);

            string keycloakUserId;
            try
            {
                keycloakUserId = await kc.CreateUserAsync(
                    username: body.AdminUsername,
                    email: body.AdminEmail ?? string.Empty,
                    tenantId: body.Id,
                    realmRoles: AdminRole,
                    sendVerifyEmail: !string.IsNullOrWhiteSpace(body.AdminEmail),
                    ct: ct).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(
                    title: "Keycloak provisioning failed",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status502BadGateway);
            }

            return Results.Created($"/tenants/{body.Id}", new CreatedTenantResponse(record, keycloakUserId));
        })
        .WithName("TenantsCreate")
        .WithSummary("Provision a tenant + admin user (admin only)")
        .WithTags("Tenants")
        .RequireAuthorization("Admin");

        // Self-service sign-up: any anonymous caller may provision a new tenant + admin user. Open
        // by design for OSS dev; production deployments should put this behind CAPTCHA / rate-limit
        // or close it and rely on admin-driven /tenants instead.
        app.MapPost("/tenants/register", async (
            RegisterTenantRequest body,
            ITenantsRepository repo,
            IKeycloakAdminClient kc,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.TenantId) ||
                string.IsNullOrWhiteSpace(body.Username) ||
                string.IsNullOrWhiteSpace(body.Password))
            {
                return Results.BadRequest(new { error = "tenantId, username and password are required" });
            }

            var existing = await repo.GetAsync(body.TenantId, ct).ConfigureAwait(false);
            if (existing is not null)
            {
                return Results.Conflict(new { error = $"Tenant '{body.TenantId}' already exists" });
            }

            var record = new TenantRecord(
                body.TenantId,
                string.IsNullOrWhiteSpace(body.TenantName) ? body.TenantId : body.TenantName,
                DateTimeOffset.UtcNow);
            await repo.AddAsync(record, ct).ConfigureAwait(false);

            string keycloakUserId;
            try
            {
                keycloakUserId = await kc.CreateUserAsync(
                    username: body.Username,
                    email: body.Email ?? string.Empty,
                    tenantId: body.TenantId,
                    realmRoles: AdminRole,
                    sendVerifyEmail: false,
                    password: body.Password,
                    ct: ct).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(
                    title: "Keycloak provisioning failed",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status502BadGateway);
            }

            return Results.Created($"/tenants/{body.TenantId}", new RegisteredTenantResponse(record, keycloakUserId));
        })
        .WithName("TenantsRegister")
        .WithSummary("Self-service sign-up: create a tenant + admin user (public)")
        .WithTags("Tenants")
        .AllowAnonymous();

        app.MapPost("/tenants/{tenantId}/members", async (
            string tenantId,
            InviteMemberRequest body,
            ITenantsRepository repo,
            IKeycloakAdminClient kc,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Username))
            {
                return Results.BadRequest(new { error = "username is required" });
            }
            var existing = await repo.GetAsync(tenantId, ct).ConfigureAwait(false);
            if (existing is null)
            {
                return Results.NotFound(new { error = $"Tenant '{tenantId}' not found" });
            }
            var roles = body.Roles is { Count: > 0 }
                ? body.Roles.Where(r => r is "admin" or "member").Distinct(StringComparer.Ordinal).ToList()
                : new List<string> { "member" };
            try
            {
                var userId = await kc.CreateUserAsync(
                    username: body.Username,
                    email: body.Email ?? string.Empty,
                    tenantId: tenantId,
                    realmRoles: roles,
                    sendVerifyEmail: !string.IsNullOrWhiteSpace(body.Email),
                    ct: ct).ConfigureAwait(false);
                return Results.Created($"/tenants/{tenantId}/members/{userId}", new InvitedMemberResponse(userId, body.Username, roles));
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(
                    title: "Keycloak provisioning failed",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status502BadGateway);
            }
        })
        .WithName("TenantsInviteMember")
        .WithSummary("Invite a member into a tenant (admin only)")
        .WithTags("Tenants")
        .RequireAuthorization("Admin");

        return app;
    }

    private static readonly string[] AdminRole = new[] { "admin" };
}

/// <summary>Response shape for GET /tenants/me.</summary>
public sealed record TenantMeResponse(
    string TenantId,
    string? UserId,
    string? UserName,
    IReadOnlyList<string> Roles,
    bool IsAuthenticated,
    bool IsAdmin);

/// <summary>Body for POST /tenants.</summary>
public sealed record CreateTenantRequest(string Id, string? Name, string AdminUsername, string? AdminEmail);

/// <summary>Response for POST /tenants.</summary>
public sealed record CreatedTenantResponse(TenantRecord Tenant, string KeycloakUserId);

/// <summary>Body for POST /tenants/{id}/members.</summary>
public sealed record InviteMemberRequest(string Username, string? Email, IReadOnlyList<string>? Roles);

/// <summary>Response for POST /tenants/{id}/members.</summary>
public sealed record InvitedMemberResponse(string KeycloakUserId, string Username, IReadOnlyList<string> Roles);

/// <summary>Body for POST /tenants/register.</summary>
public sealed record RegisterTenantRequest(
    string TenantId,
    string? TenantName,
    string Username,
    string? Email,
    string Password);

/// <summary>Response for POST /tenants/register.</summary>
public sealed record RegisteredTenantResponse(TenantRecord Tenant, string KeycloakUserId);
