// Opens a GitHub PR from a PipelineResult. Reads PAT + target repo from IRuntimeOverrides
// (tenant-scoped — each tenant carries its own credentials). GitHub Enterprise is supported
// via the GitHubBaseUrl override; null / blank falls back to the public github.com host.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Pipeline;
using AgentOs.Modules.Llm;
using Microsoft.Extensions.Logging;
using Octokit;

namespace AgentOs.Modules.Integration;

/// <inheritdoc cref="IGitHubPrService"/>
public sealed class GitHubPrService : IGitHubPrService
{
    private const string UserAgent = "agentos";
    private const string DefaultBranchPrefix = "agentos";

    private readonly IRuntimeOverrides _overrides;
    private readonly ILogger<GitHubPrService> _logger;

    public GitHubPrService(IRuntimeOverrides overrides, ILogger<GitHubPrService> logger)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        ArgumentNullException.ThrowIfNull(logger);
        _overrides = overrides;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<GitHubPrResult> OpenPrAsync(PipelineResult result, string title, string body, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var pat = _overrides.GitHubPat;
        var owner = _overrides.GitHubRepoOwner;
        var name = _overrides.GitHubRepoName;
        var baseBranch = string.IsNullOrWhiteSpace(_overrides.GitHubBaseBranch) ? "main" : _overrides.GitHubBaseBranch!;
        var baseUrl = _overrides.GitHubBaseUrl;

        var missing = new List<string>(3);
        if (string.IsNullOrWhiteSpace(pat)) { missing.Add("GitHubPat"); }
        if (string.IsNullOrWhiteSpace(owner)) { missing.Add("GitHubRepoOwner"); }
        if (string.IsNullOrWhiteSpace(name)) { missing.Add("GitHubRepoName"); }
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"GitHub integration is not configured for this tenant. Missing: {string.Join(", ", missing)}. " +
                "Set these on the /admin or Settings page first; the values are stored per tenant.");
        }

        ArgumentNullException.ThrowIfNull(result);

        var client = string.IsNullOrWhiteSpace(baseUrl)
            ? new GitHubClient(new ProductHeaderValue(UserAgent)) { Credentials = new Credentials(pat) }
            : new GitHubClient(new ProductHeaderValue(UserAgent), new Uri(baseUrl)) { Credentials = new Credentials(pat) };

        // 1. Locate base branch SHA.
        ct.ThrowIfCancellationRequested();
        Reference baseRef;
        try
        {
            baseRef = await client.Git.Reference.Get(owner, name, $"heads/{baseBranch}").ConfigureAwait(false);
        }
        catch (NotFoundException ex)
        {
            throw new InvalidOperationException(
                $"GitHub base branch '{baseBranch}' not found on {owner}/{name}. Set GitHubBaseBranch on Settings or create the branch first.", ex);
        }
        catch (AuthorizationException ex)
        {
            throw new InvalidOperationException(
                $"GitHub rejected the PAT for {owner}/{name}. Check the token's scopes (needs 'repo') and that it isn't expired.", ex);
        }
        var baseSha = baseRef.Object.Sha;

        // 2. Create a new timestamped branch off the base. Prefix is hardcoded `agentos/` so
        // operators can clean up auto-generated branches with a single glob.
        var branch = $"{DefaultBranchPrefix}/{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        ct.ThrowIfCancellationRequested();
        await client.Git.Reference.Create(owner, name, new NewReference($"refs/heads/{branch}", baseSha)).ConfigureAwait(false);
        _logger.LogInformation("Created branch {Branch} on {Owner}/{Name}", branch, owner, name);

        // 3. Commit every generated file.
        var files = new List<(string Path, string Content)>();
        if (result.Code?.Files is not null)
        {
            files.AddRange(result.Code.Files.Select(f => (f.Path, f.Content)));
        }
        if (result.Tests?.Files is not null)
        {
            files.AddRange(result.Tests.Files.Select(f => (f.Path, f.Content)));
        }

        foreach (var (path, content) in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await client.Repository.Content.CreateFile(owner, name, path,
                    new CreateFileRequest($"feat: add {path}", content, branch)).ConfigureAwait(false);
            }
            catch (ApiValidationException)
            {
                // File exists on the branch — fetch SHA and update.
                var existing = await client.Repository.Content.GetAllContentsByRef(owner, name, path, branch).ConfigureAwait(false);
                if (existing.Count > 0)
                {
                    await client.Repository.Content.UpdateFile(owner, name, path,
                        new UpdateFileRequest($"feat: update {path}", content, existing[0].Sha, branch)).ConfigureAwait(false);
                }
            }
        }

        // 4. Open the PR.
        ct.ThrowIfCancellationRequested();
        var pr = await client.PullRequest.Create(owner, name, new NewPullRequest(title, branch, baseBranch)
        {
            Body = body,
        }).ConfigureAwait(false);

        _logger.LogInformation("Opened PR #{Number} ({Url})", pr.Number, pr.HtmlUrl);
        return new GitHubPrResult(pr.Number, pr.HtmlUrl, branch);
    }
}
