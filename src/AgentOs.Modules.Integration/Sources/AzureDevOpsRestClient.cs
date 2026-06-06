// Azure DevOps via its REST API + HttpClient — NOT the official client SDK (which does not resolve under
// net10, the blocker that kept this provider a stub). Same approach the GitHub Projects client takes:
// raw HTTP + a pure, unit-testable response parser. Covers the repo-connection basics (list + validate);
// boards / work items remain a separate milestone.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Workspaces;

namespace AgentOs.Modules.Integration.Sources;

internal sealed class AzureDevOpsRestClient
{
    private const string ApiVersion = "7.1";
    private readonly HttpClient _http;

    public AzureDevOpsRestClient(HttpClient http) => _http = http ?? throw new ArgumentNullException(nameof(http));

    // PAT auth is HTTP Basic with an empty username and the PAT as the password.
    private static AuthenticationHeaderValue Basic(string pat)
        => new("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + pat)));

    /// <summary>Lists every repository the PAT can see across the org's projects.</summary>
    public async Task<IReadOnlyList<RemoteRepo>> ListRepositoriesAsync(string host, string org, string pat, CancellationToken ct)
    {
        var url = $"https://{host}/{Uri.EscapeDataString(org)}/_apis/git/repositories?api-version={ApiVersion}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url) { Headers = { Authorization = Basic(pat) } };
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ParseRepositories(org, json);
    }

    /// <summary>Validates one repo and resolves its default branch.</summary>
    public async Task<RepoValidation> ValidateAsync(string host, string org, string project, string repo, string pat, CancellationToken ct)
    {
        var proj = string.IsNullOrWhiteSpace(project) ? org : project;
        var url = $"https://{host}/{Uri.EscapeDataString(org)}/{Uri.EscapeDataString(proj)}/_apis/git/repositories/{Uri.EscapeDataString(repo)}?api-version={ApiVersion}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url) { Headers = { Authorization = Basic(pat) } };
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return RepoValidation.Fail(
                $"Azure DevOps returned {(int)response.StatusCode} for {org}/{proj}/{repo}. Check the org/project/repo and a PAT with Code (Read).");
        }
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var branch = StripRef(doc.RootElement.TryGetProperty("defaultBranch", out var db) ? db.GetString() : null);
        return RepoValidation.Success(branch ?? "main");
    }

    /// <summary>Pure parser for the <c>GET .../git/repositories</c> response — unit-testable, no network.</summary>
    public static IReadOnlyList<RemoteRepo> ParseRepositories(string org, string json)
    {
        var repos = new List<RemoteRepo>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return repos;
        }

        foreach (var r in value.EnumerateArray())
        {
            var name = r.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var project = r.TryGetProperty("project", out var p) && p.TryGetProperty("name", out var pn) ? pn.GetString() : null;
            var branch = StripRef(r.TryGetProperty("defaultBranch", out var db) ? db.GetString() : null) ?? "main";
            var remoteUrl = r.TryGetProperty("remoteUrl", out var ru) ? ru.GetString() ?? string.Empty : string.Empty;

            repos.Add(new RemoteRepo(
                Owner: org,
                Name: name,
                FullName: string.IsNullOrEmpty(project) ? $"{org}/{name}" : $"{org}/{project}/{name}",
                DefaultBranch: branch,
                RemoteUrl: remoteUrl,
                Private: true,
                Project: project));
        }
        return repos;
    }

    /// <summary>Strips the <c>refs/heads/</c> prefix Azure DevOps returns on default branches.</summary>
    internal static string? StripRef(string? branchRef)
        => string.IsNullOrEmpty(branchRef)
            ? branchRef
            : branchRef.StartsWith("refs/heads/", StringComparison.Ordinal) ? branchRef["refs/heads/".Length..] : branchRef;
}
