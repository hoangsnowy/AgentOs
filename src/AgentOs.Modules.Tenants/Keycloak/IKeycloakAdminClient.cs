// Thin abstraction over the Keycloak Admin REST API: enough to provision a tenant's admin user
// and to invite additional members. Implemented as a typed HttpClient.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AgentOs.Modules.Tenants.Keycloak;

/// <summary>Provisioning operations against a Keycloak realm.</summary>
public interface IKeycloakAdminClient
{
    /// <summary>Create a user with username + email + a single <c>tenant</c> attribute, grant the
    /// listed realm roles, and optionally seed a password or trigger Keycloak's reset-password
    /// email so the invitee picks the initial password themselves. Returns the new Keycloak user id.</summary>
    Task<string> CreateUserAsync(
        string username,
        string email,
        string tenantId,
        IReadOnlyList<string> realmRoles,
        bool sendVerifyEmail,
        string? password = null,
        CancellationToken ct = default);

    /// <summary>Delete a Keycloak user by id. Used by the signup saga to roll back a half-created
    /// user when the downstream registry write throws. Throws on any non-success response.</summary>
    Task DeleteUserAsync(string userId, CancellationToken ct = default);
}
