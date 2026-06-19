// The dev / CI fallback for IKeycloakAdminClient (used when no Keycloak server is configured). Every
// operation must degrade to a safe no-op / empty result and never throw, so the Users app boots and
// renders instead of crashing the circuit.

using System.Threading.Tasks;
using AgentOs.Modules.Tenants.Keycloak;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Tenants;

public sealed class NullKeycloakAdminClientTests
{
    private readonly NullKeycloakAdminClient _sut = new();

    [Fact]
    public async Task ListUsersByTenantAsync_ReturnsEmpty()
    {
        var users = await _sut.ListUsersByTenantAsync("tenant-1");
        users.ShouldBeEmpty();
    }

    [Fact]
    public async Task CreateUserAsync_ReturnsAStableDevId()
    {
        var id = await _sut.CreateUserAsync("alice", "a@x.com", "tenant-1", ["member"], sendVerifyEmail: false);
        id.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetUserTenantAsync_ReturnsNull()
    {
        // No Keycloak ⇒ no user to confirm; the cross-tenant guard treats this as not-in-tenant.
        (await _sut.GetUserTenantAsync("user-1")).ShouldBeNull();
    }

    [Fact]
    public async Task WriteOperations_AreNoOps_AndDoNotThrow()
    {
        // None of these should throw — they complete silently in the absence of a Keycloak server.
        await Should.NotThrowAsync(async () =>
        {
            await _sut.DeleteUserAsync("user-1");
            await _sut.UpdateUserRolesAsync("user-1", ["admin", "member"]);
            await _sut.SendPasswordResetEmailAsync("user-1");
        });
    }
}
