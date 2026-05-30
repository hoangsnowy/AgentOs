// Coverage for the two admin-REST verbs added during Epic D: DeleteUserAsync (signup-saga
// rollback) and ListUsersByTenantAsync (tenant admin members page). Same stub-handler pattern
// as KeycloakAdminClientTests.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Modules.Tenants.Keycloak;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Identity;

public sealed class KeycloakAdminClientListDeleteTests
{
    private static readonly string[] ExpectedIds = { "u-1", "u-3" };

    [Fact]
    public async Task DeleteUserAsync_NoContent_Succeeds()
    {
        var handler = new StubHandler();
        handler.Enqueue(_ => true, Json("{\"access_token\":\"tok\",\"expires_in\":300}"));
        handler.Enqueue(req => req.Method == HttpMethod.Delete && req.RequestUri!.AbsolutePath.EndsWith("/admin/realms/agentic/users/u-1"),
            new HttpResponseMessage(HttpStatusCode.NoContent));

        var client = NewClient(handler);
        await client.DeleteUserAsync("u-1");
        handler.Calls.Count.ShouldBe(2);
    }

    [Fact]
    public async Task DeleteUserAsync_NotFound_DoesNotThrow()
    {
        var handler = new StubHandler();
        handler.Enqueue(_ => true, Json("{\"access_token\":\"tok\",\"expires_in\":300}"));
        handler.Enqueue(_ => true, new HttpResponseMessage(HttpStatusCode.NotFound));

        var client = NewClient(handler);
        await client.DeleteUserAsync("u-missing"); // swallowed by design — saga must converge
    }

    [Fact]
    public async Task DeleteUserAsync_500_Throws()
    {
        var handler = new StubHandler();
        handler.Enqueue(_ => true, Json("{\"access_token\":\"tok\",\"expires_in\":300}"));
        handler.Enqueue(_ => true, new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("boom"),
        });

        var client = NewClient(handler);
        await Should.ThrowAsync<InvalidOperationException>(async () => await client.DeleteUserAsync("u-1"));
    }

    [Fact]
    public async Task ListUsersByTenantAsync_FiltersByTenantAttribute_AndFetchesRoles()
    {
        var handler = new StubHandler();
        handler.Enqueue(_ => true, Json("{\"access_token\":\"tok\",\"expires_in\":300}"));
        // GET /users — return two users, only one is in tenant 'acme'.
        handler.Enqueue(req => req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.EndsWith("/admin/realms/agentic/users"),
            Json("""
            [
              {"id":"u-1","username":"alice","email":"alice@x.test","enabled":true,"emailVerified":true,"attributes":{"tenant":["acme"]}},
              {"id":"u-2","username":"bob","email":"bob@x.test","enabled":true,"emailVerified":true,"attributes":{"tenant":["other"]}},
              {"id":"u-3","username":"carol","email":null,"enabled":false,"emailVerified":false,"attributes":{"tenant":["acme"]}}
            ]
            """));
        // GET /users/u-1/role-mappings/realm
        handler.Enqueue(req => req.RequestUri!.AbsolutePath.EndsWith("/users/u-1/role-mappings/realm"),
            Json("[{\"id\":\"r-admin\",\"name\":\"admin\"}]"));
        // GET /users/u-3/role-mappings/realm
        handler.Enqueue(req => req.RequestUri!.AbsolutePath.EndsWith("/users/u-3/role-mappings/realm"),
            Json("[{\"id\":\"r-member\",\"name\":\"member\"}]"));

        var client = NewClient(handler);
        var members = await client.ListUsersByTenantAsync("acme");

        members.Count.ShouldBe(2);
        members.Select(m => m.Id).OrderBy(s => s).ShouldBe(ExpectedIds);
        members.First(m => m.Id == "u-1").Roles.ShouldContain("admin");
        members.First(m => m.Id == "u-3").Enabled.ShouldBeFalse();
        members.First(m => m.Id == "u-3").EmailVerified.ShouldBeFalse();
    }

    private static KeycloakAdminClient NewClient(StubHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://kc/") };
        return new KeycloakAdminClient(http,
            Options.Create(new KeycloakAdminOptions { BaseUrl = "http://kc", Realm = "agentic" }),
            NullLogger<KeycloakAdminClient>.Instance);
    }

    private static HttpResponseMessage Json(string body)
    {
        var c = new StringContent(body, Encoding.UTF8);
        c.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = c };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<(Func<HttpRequestMessage, bool> Match, HttpResponseMessage Response)> _expected = new();
        public List<HttpRequestMessage> Calls { get; } = new();

        public void Enqueue(Func<HttpRequestMessage, bool> match, HttpResponseMessage response)
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
            return Task.FromResult(match(request)
                ? response
                : new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("Mismatched request: " + request.RequestUri),
                });
        }
    }
}
