// Exercises KeycloakAdminClient end-to-end against a stubbed HttpMessageHandler. Verifies
// the admin-token request, the user-create POST, the role-mapping flow, and the verify-email
// action are issued in the right order with the bearer header attached.

using System.Net;
using AgentOs.Modules.Tenants.Keycloak;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Identity;

public sealed class KeycloakAdminClientTests
{
    private static readonly string[] AdminRole = ["admin"];


    [Fact]
    public async Task CreateUserAsync_HappyPath_LoginsCreatesGrantsRolesAndSendsEmail()
    {
        var handler = new StubHandler();
        // 1. Login
        handler.Enqueue(req =>
            req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath.EndsWith("/realms/master/protocol/openid-connect/token"),
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent("{\"access_token\":\"tok-1\",\"expires_in\":300}"),
            });
        // 2. Create user
        handler.Enqueue(req =>
            req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath.EndsWith("/admin/realms/agentic/users"),
            new HttpResponseMessage(HttpStatusCode.Created)
            {
                Headers = { Location = new Uri("http://kc/admin/realms/agentic/users/u-123") },
            });
        // 3. GET role "admin"
        handler.Enqueue(req =>
            req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.EndsWith("/admin/realms/agentic/roles/admin"),
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent("{\"id\":\"role-admin\",\"name\":\"admin\"}"),
            });
        // 4. POST role mappings
        handler.Enqueue(req =>
            req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath.EndsWith("/admin/realms/agentic/users/u-123/role-mappings/realm"),
            new HttpResponseMessage(HttpStatusCode.NoContent));
        // 5. PUT execute-actions-email
        handler.Enqueue(req =>
            req.Method == HttpMethod.Put && req.RequestUri!.AbsolutePath.EndsWith("/admin/realms/agentic/users/u-123/execute-actions-email"),
            new HttpResponseMessage(HttpStatusCode.NoContent));

        var http = new HttpClient(handler) { BaseAddress = new Uri("http://kc/") };
        var client = new KeycloakAdminClient(
            http,
            Options.Create(new KeycloakAdminOptions { BaseUrl = "http://kc", Realm = "agentic" }),
            NullLogger<KeycloakAdminClient>.Instance);

        var userId = await client.CreateUserAsync(
            username: "alice",
            email: "alice@example.com",
            tenantId: "acme",
            realmRoles: AdminRole,
            sendVerifyEmail: true);

        userId.ShouldBe("u-123");
        handler.Calls.Count.ShouldBe(5);
        handler.Calls[1].Headers.Authorization.ShouldBe(new AuthenticationHeaderValue("Bearer", "tok-1"));
    }

    [Fact]
    public async Task CreateUserAsync_TokenCached_BackToBackCallsLogInOnce()
    {
        var handler = new StubHandler();
        handler.Enqueue(_ => true, new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent("{\"access_token\":\"tok-1\",\"expires_in\":300}"),
        });
        handler.Enqueue(_ => true, new HttpResponseMessage(HttpStatusCode.Created)
        {
            Headers = { Location = new Uri("http://kc/admin/realms/agentic/users/u-1") },
        });
        handler.Enqueue(_ => true, new HttpResponseMessage(HttpStatusCode.Created)
        {
            Headers = { Location = new Uri("http://kc/admin/realms/agentic/users/u-2") },
        });

        var http = new HttpClient(handler) { BaseAddress = new Uri("http://kc/") };
        var client = new KeycloakAdminClient(
            http,
            Options.Create(new KeycloakAdminOptions { BaseUrl = "http://kc", Realm = "agentic" }),
            NullLogger<KeycloakAdminClient>.Instance);

        await client.CreateUserAsync("u1", string.Empty, "acme", System.Array.Empty<string>(), sendVerifyEmail: false);
        await client.CreateUserAsync("u2", string.Empty, "acme", System.Array.Empty<string>(), sendVerifyEmail: false);

        // 1 login + 2 user-creates, no second login.
        handler.Calls.Count.ShouldBe(3);
        handler.Calls[0].RequestUri!.AbsolutePath.ShouldContain("/realms/master/protocol/openid-connect/token");
        handler.Calls[1].RequestUri!.AbsolutePath.ShouldEndWith("/users");
        handler.Calls[2].RequestUri!.AbsolutePath.ShouldEndWith("/users");
    }

    private static StringContent JsonContent(string body)
    {
        var c = new StringContent(body, System.Text.Encoding.UTF8);
        c.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return c;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly System.Collections.Generic.Queue<(System.Func<HttpRequestMessage, bool> Match, HttpResponseMessage Response)> _expected = new();
        public System.Collections.Generic.List<HttpRequestMessage> Calls { get; } = new();

        public void Enqueue(System.Func<HttpRequestMessage, bool> match, HttpResponseMessage response)
            => _expected.Enqueue((match, response));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add(request);
            if (_expected.Count == 0)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("Unexpected request: " + request.RequestUri),
                });
            }
            var (match, response) = _expected.Dequeue();
            if (!match(request))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("Request did not match expectation: " + request.RequestUri),
                });
            }
            return Task.FromResult(response);
        }
    }
}
