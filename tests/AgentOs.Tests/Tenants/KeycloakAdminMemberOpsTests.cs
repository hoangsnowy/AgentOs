// Tenant-admin member ops (UsersApp follow-up): UpdateUserRolesAsync diff logic +
// SendPasswordResetEmailAsync. Uses a self-contained recording HttpMessageHandler so the test
// has zero dependency on shared helpers — it asserts on the exact Keycloak Admin REST calls made.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Modules.Tenants.Keycloak;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Tenants;

public sealed class KeycloakAdminMemberOpsTests
{
    private const string Realm = "agentic";
    private const string UserId = "user-123";

    private static readonly string[] RolesAdmin = ["admin"];
    private static readonly string[] RolesAdminPlusUnmanaged = ["admin", "superuser"];

    [Fact]
    public async Task UpdateUserRoles_GrantsMissing_AndRevokesManagedNoLongerDesired()
    {
        // Current mappings: member only. Desired: admin only → grant admin, revoke member.
        var recorder = new Recorder();
        recorder.RealmRolesForUser = new() { new("rid-member", "member") };
        recorder.RoleByName = new()
        {
            ["admin"] = new("rid-admin", "admin"),
            ["member"] = new("rid-member", "member"),
        };
        var client = Build(recorder);

        await client.UpdateUserRolesAsync(UserId, RolesAdmin);

        // POST grant admin
        recorder.Posts.ShouldContain(p =>
            p.Path == $"admin/realms/{Realm}/users/{UserId}/role-mappings/realm" && p.Body.Contains("admin"));
        // DELETE revoke member
        recorder.Deletes.ShouldContain(d =>
            d.Path == $"admin/realms/{Realm}/users/{UserId}/role-mappings/realm" && d.Body.Contains("member"));
    }

    [Fact]
    public async Task UpdateUserRoles_NoChange_WhenDesiredEqualsCurrent()
    {
        var recorder = new Recorder();
        recorder.RealmRolesForUser = new() { new("rid-admin", "admin") };
        recorder.RoleByName = new() { ["admin"] = new("rid-admin", "admin") };
        var client = Build(recorder);

        await client.UpdateUserRolesAsync(UserId, RolesAdmin);

        // No grant POST and no revoke DELETE against the role-mappings endpoint.
        recorder.Posts.ShouldNotContain(p => p.Path.EndsWith("role-mappings/realm"));
        recorder.Deletes.ShouldNotContain(d => d.Path.EndsWith("role-mappings/realm"));
    }

    [Fact]
    public async Task UpdateUserRoles_IgnoresUnmanagedRolesInDesiredSet()
    {
        // "superuser" isn't a managed role → must never be granted; admin still applied.
        var recorder = new Recorder();
        recorder.RealmRolesForUser = new() { new("rid-member", "member") };
        recorder.RoleByName = new()
        {
            ["admin"] = new("rid-admin", "admin"),
            ["member"] = new("rid-member", "member"),
        };
        var client = Build(recorder);

        await client.UpdateUserRolesAsync(UserId, RolesAdminPlusUnmanaged);

        recorder.RoleLookups.ShouldNotContain("superuser");
        recorder.RoleLookups.ShouldContain("admin");
    }

    [Fact]
    public async Task SendPasswordResetEmail_PutsUpdatePasswordAction()
    {
        var recorder = new Recorder();
        var client = Build(recorder);

        await client.SendPasswordResetEmailAsync(UserId);

        var put = recorder.Puts.ShouldHaveSingleItem();
        put.Path.ShouldBe($"admin/realms/{Realm}/users/{UserId}/execute-actions-email");
        put.Body.ShouldContain("UPDATE_PASSWORD");
    }

    [Fact]
    public async Task SendPasswordResetEmail_Throws_OnKeycloakError()
    {
        var recorder = new Recorder { FailActionsEmail = true };
        var client = Build(recorder);

        await Should.ThrowAsync<InvalidOperationException>(() => client.SendPasswordResetEmailAsync(UserId));
    }

    [Fact]
    public async Task GetUserTenant_ReturnsTheTenantAttribute()
    {
        var recorder = new Recorder { UserTenant = "tenant-b" };
        var client = Build(recorder);

        (await client.GetUserTenantAsync(UserId)).ShouldBe("tenant-b");
    }

    [Fact]
    public async Task GetUserTenant_NotFound_ReturnsNull()
    {
        var recorder = new Recorder { UserNotFound = true };
        var client = Build(recorder);

        // null drives the cross-tenant guard to reject (target not confirmed in the caller's tenant).
        (await client.GetUserTenantAsync(UserId)).ShouldBeNull();
    }

    private static KeycloakAdminClient Build(Recorder recorder)
    {
        var http = new HttpClient(recorder) { BaseAddress = new Uri("http://kc.local/") };
        var opts = Options.Create(new KeycloakAdminOptions
        {
            BaseUrl = "http://kc.local",
            Realm = Realm,
            ClientId = "admin-cli",
            Username = "admin",
            Password = "admin",
        });
        return new KeycloakAdminClient(http, opts, NullLogger<KeycloakAdminClient>.Instance);
    }

    private sealed record Captured(string Path, string Body);
    private sealed record RoleDto(string id, string name);

    private sealed class Recorder : HttpMessageHandler
    {
        public List<RoleDto> RealmRolesForUser { get; set; } = new();
        public Dictionary<string, RoleDto> RoleByName { get; set; } = new();
        public bool FailActionsEmail { get; set; }
        public string? UserTenant { get; set; }
        public bool UserNotFound { get; set; }

        public List<Captured> Posts { get; } = new();
        public List<Captured> Deletes { get; } = new();
        public List<Captured> Puts { get; } = new();
        public List<string> RoleLookups { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);

            // Token endpoint.
            if (path.Contains("protocol/openid-connect/token"))
            {
                return Json(new { access_token = "tok", expires_in = 300 });
            }

            // GET current realm role mappings for the user.
            if (request.Method == HttpMethod.Get && path.EndsWith($"users/{UserId}/role-mappings/realm"))
            {
                return Json(RealmRolesForUser);
            }

            // GET a single user (tenant-attribute lookup for the cross-tenant guard).
            if (request.Method == HttpMethod.Get && path.EndsWith($"users/{UserId}"))
            {
                if (UserNotFound)
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }
                return Json(new
                {
                    id = UserId,
                    attributes = UserTenant is null
                        ? null
                        : new Dictionary<string, string[]> { ["tenant"] = new[] { UserTenant } },
                });
            }

            // GET role by name.
            if (request.Method == HttpMethod.Get && path.Contains("/roles/"))
            {
                var name = Uri.UnescapeDataString(path.Split("/roles/").Last());
                RoleLookups.Add(name);
                return RoleByName.TryGetValue(name, out var dto) ? Json(dto) : new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("role-mappings/realm"))
            {
                Posts.Add(new Captured(path, body));
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (request.Method == HttpMethod.Delete && path.EndsWith("role-mappings/realm"))
            {
                Deletes.Add(new Captured(path, body));
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (request.Method == HttpMethod.Put && path.EndsWith("execute-actions-email"))
            {
                Puts.Add(new Captured(path, body));
                return new HttpResponseMessage(FailActionsEmail ? HttpStatusCode.InternalServerError : HttpStatusCode.NoContent);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        private static HttpResponseMessage Json(object payload) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };
    }
}
