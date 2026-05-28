// AgenticSdlc.Infrastructure/Integration/GitHubPrService.cs
// IGitHubPrService implementation using Octokit. Creates a timestamped branch, commits the
// generated code + test files, and opens a PR against the configured base branch.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Application.Configuration;
using AgenticSdlc.Application.Integration;
using AgenticSdlc.Domain.Pipeline;
using Microsoft.Extensions.Logging;
using Octokit;

namespace AgenticSdlc.Infrastructure.Integration;

/// <inheritdoc cref="IGitHubPrService"/>
public sealed class GitHubPrService : IGitHubPrService
{
    private const string UserAgent = "agentic-sdlc-net";

    private readonly IRuntimeOverrides _overrides;
    private readonly ILogger<GitHubPrService> _logger;

    /// <summary>Initializes the service with the runtime override store (for PAT + repo) and a logger.</summary>
    public GitHubPrService(IRuntimeOverrides overrides, ILogger<GitHubPrService> logger)
    {
        _overrides = overrides ?? throw new ArgumentNullException(nameof(overrides));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<GitHubPrResult> OpenPrAsync(PipelineResult result, string title, string body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(result);

        var pat = _overrides.GitHubPat;
        var owner = _overrides.GitHubRepoOwner;
        var name = _overrides.GitHubRepoName;
        var baseBranch = string.IsNullOrWhiteSpace(_overrides.GitHubBaseBranch) ? "main" : _overrides.GitHubBaseBranch!;

        if (string.IsNullOrWhiteSpace(pat) || string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException(
                "GitHub PAT and target repo (owner / name) must be set on the Settings page before opening a PR.");
        }

        var client = new GitHubClient(new ProductHeaderValue(UserAgent))
        {
            Credentials = new Credentials(pat),
        };

        // 1. Locate base branch SHA.
        ct.ThrowIfCancellationRequested();
        var baseRef = await client.Git.Reference.Get(owner, name, $"heads/{baseBranch}").ConfigureAwait(false);
        var baseSha = baseRef.Object.Sha;

        // 2. Create a new timestamped branch off the base.
        var branch = $"agentic-sdlc/{DateTime.UtcNow:yyyyMMdd-HHmmss}";
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
