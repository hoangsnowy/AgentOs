// Fetches GitHub Issues via Octokit. Same PAT / client-creation pattern as GitHubSourceProvider
// (per-workspace token from WorkspaceDescriptor, optional Enterprise host). PRs are excluded
// because GitHub's Issues API returns both issues and pull requests.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Workspaces;
using Octokit;

namespace AgentOs.Modules.Integration;

/// <summary><see cref="IGitHubIssueService"/> backed by Octokit.</summary>
public sealed class GitHubIssueService : IGitHubIssueService
{
    private const string UserAgent = "agentos";

    /// <inheritdoc />
    public async Task<IReadOnlyList<GitHubIssue>> ListOpenIssuesAsync(
        WorkspaceDescriptor workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var client = CreateClient(workspace.AccessToken, workspace.Host);
        IReadOnlyList<Issue> raw;
        try
        {
            raw = await client.Issue.GetAllForRepository(
                workspace.Owner,
                workspace.Repo,
                new RepositoryIssueRequest { State = ItemStateFilter.Open },
                new ApiOptions { PageSize = 50, PageCount = 1 })
                .ConfigureAwait(false);
        }
        catch (AuthorizationException ex)
        {
            throw new InvalidOperationException(
                $"GitHub rejected the token for {workspace.Owner}/{workspace.Repo}. Check the PAT has 'repo' scope.", ex);
        }
        catch (NotFoundException ex)
        {
            throw new InvalidOperationException(
                $"Repository {workspace.Owner}/{workspace.Repo} not found (or the token cannot see it).", ex);
        }

        return raw
            .Where(i => i.PullRequest is null)
            .Select(i => new GitHubIssue(
                Number: i.Number,
                Title: i.Title,
                State: i.State.StringValue,
                HtmlUrl: i.HtmlUrl,
                AssigneeLogin: i.Assignee?.Login,
                Labels: i.Labels.Select(l => l.Name).ToList()))
            .ToList();
    }

    private static GitHubClient CreateClient(string token, string? host)
    {
        var header = new ProductHeaderValue(UserAgent);
        var client = string.IsNullOrWhiteSpace(host)
            ? new GitHubClient(header)
            : new GitHubClient(header, new Uri(host));
        client.Credentials = new Credentials(token);
        return client;
    }
}
