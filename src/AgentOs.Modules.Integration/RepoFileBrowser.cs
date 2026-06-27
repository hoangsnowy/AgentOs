// Read-only repo file browser for the desktop Files app: list a path's entries + fetch a file's text
// over the GitHub Contents API, reusing the shared SSRF-hardened GitHubClientFactory. The caller passes
// the (per-workspace) PAT; credentials stay per-call so two tenants never share a token.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Octokit;

namespace AgentOs.Modules.Integration;

/// <summary>One entry in a repository directory listing.</summary>
public sealed record RepoEntry(string Name, string Path, bool IsDir, long Size);

/// <summary>Read-only browse of a connected GitHub repo's files (the desktop Files app).</summary>
public interface IRepoFileBrowser
{
    /// <summary>List the entries at <paramref name="path"/> (empty = repo root) on <paramref name="branch"/>,
    /// directories first then files, alphabetical.</summary>
    Task<IReadOnlyList<RepoEntry>> ListAsync(
        string owner, string repo, string path, string branch, string pat, CancellationToken ct = default);

    /// <summary>Fetch a single file's decoded text content.</summary>
    Task<string> GetTextAsync(
        string owner, string repo, string path, string branch, string pat, CancellationToken ct = default);
}

internal sealed class GitHubRepoFileBrowser : IRepoFileBrowser
{
    public async Task<IReadOnlyList<RepoEntry>> ListAsync(
        string owner, string repo, string path, string branch, string pat, CancellationToken ct = default)
    {
        var client = GitHubClientFactory.Create(pat);
        var contents = string.IsNullOrEmpty(path)
            ? await client.Repository.Content.GetAllContentsByRef(owner, repo, branch).ConfigureAwait(false)
            : await client.Repository.Content.GetAllContentsByRef(owner, repo, path, branch).ConfigureAwait(false);

        return contents
            .Select(c => new RepoEntry(c.Name, c.Path, c.Type.Value == ContentType.Dir, c.Size))
            .OrderByDescending(e => e.IsDir)
            .ThenBy(e => e.Name, System.StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<string> GetTextAsync(
        string owner, string repo, string path, string branch, string pat, CancellationToken ct = default)
    {
        var client = GitHubClientFactory.Create(pat);
        var contents = await client.Repository.Content
            .GetAllContentsByRef(owner, repo, path, branch).ConfigureAwait(false);
        // Octokit decodes base64 text content into .Content; a directory or binary blob yields null/empty.
        return contents.Count > 0 ? contents[0].Content ?? string.Empty : string.Empty;
    }
}
