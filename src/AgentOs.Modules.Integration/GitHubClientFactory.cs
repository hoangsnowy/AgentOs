// One place that builds an authenticated Octokit client. Both PR paths (Pipeline artifacts -> PR and the
// Spine pushed-branch -> PR) plus the source provider went through their own near-identical copies of this;
// they now share it, so GitHub auth + Enterprise-host handling live in a single, tested spot.

using System;
using Octokit;

namespace AgentOs.Modules.Integration;

/// <summary>Creates an authenticated <see cref="GitHubClient"/> (public github.com or an Enterprise host).</summary>
internal static class GitHubClientFactory
{
    private const string UserAgent = "agentos";

    /// <summary>Build a client. <paramref name="host"/> null/blank → public github.com; a value → GitHub
    /// Enterprise base URL. A non-blank <paramref name="accessToken"/> is applied as the PAT credential.</summary>
    public static GitHubClient Create(string? accessToken, string? host = null)
    {
        var header = new ProductHeaderValue(UserAgent);
        var client = string.IsNullOrWhiteSpace(host)
            ? new GitHubClient(header)
            : new GitHubClient(header, new Uri(host));

        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            client.Credentials = new Credentials(accessToken);
        }
        return client;
    }
}
