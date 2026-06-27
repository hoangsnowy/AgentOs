// One place that builds an authenticated Octokit client. Both PR paths (Pipeline artifacts -> PR and the
// Board pushed-branch -> PR) plus the source provider went through their own near-identical copies of this;
// they now share it, so GitHub auth + Enterprise-host handling live in a single, tested spot.
//
// Every GitHubClient is built over ONE shared HttpClient/handler (the static HttpClientAdapter) so the
// process reuses a single connection pool instead of churning sockets — a fresh GitHubClient per call
// would otherwise allocate its own handler each time (mirrors GitHubProjectsClient's static HttpClient).
// Credentials are still per-call (InMemoryCredentialStore), so two tenants never share a token.

using System;
using AgentOs.SharedKernel.Security;
using Octokit;
using Octokit.Internal;

namespace AgentOs.Modules.Integration;

/// <summary>Creates an authenticated <see cref="GitHubClient"/> (public github.com or an Enterprise host).</summary>
internal static class GitHubClientFactory
{
    private const string UserAgent = "agentos";

    // Shared across all clients → one underlying HttpClient + connection pool for the whole process. The
    // handler is SSRF-hardened: GitHub Enterprise hosts are tenant-supplied, so refuse private/internal IPs
    // (incl. via DNS + redirects). SocketsHttpHandler : HttpMessageHandler satisfies the adapter's factory.
    private static readonly IHttpClient SharedHttp = new HttpClientAdapter(SsrfGuard.CreateHardenedHandler);
    private static readonly IJsonSerializer Serializer = new SimpleJsonSerializer();

    /// <summary>Build a client. <paramref name="host"/> null/blank → public github.com; a value → GitHub
    /// Enterprise base URL. A non-blank <paramref name="accessToken"/> is applied as the PAT credential.</summary>
    public static GitHubClient Create(string? accessToken, string? host = null)
    {
        var header = new ProductHeaderValue(UserAgent);
        var credentials = string.IsNullOrWhiteSpace(accessToken) ? Credentials.Anonymous : new Credentials(accessToken);
        var connection = new Connection(header, ResolveBaseAddress(host), new InMemoryCredentialStore(credentials), SharedHttp, Serializer);
        return new GitHubClient(connection);
    }

    // Mirrors Octokit's internal GitHubClient.FixUpBaseUri so behaviour is identical to the previous
    // `new GitHubClient(header, new Uri(host))`: public hosts collapse to the api.github.com endpoint, an
    // Enterprise host gets the `/api/v3/` path appended.
    private static Uri ResolveBaseAddress(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return GitHubClient.GitHubApiUrl;
        }

        var uri = new Uri(host);
        if (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase))
        {
            return GitHubClient.GitHubApiUrl;
        }

        return new Uri(uri, new Uri("/api/v3/", UriKind.Relative));
    }
}
