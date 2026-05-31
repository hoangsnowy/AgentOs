// M4/Spine — fetch GitHub Issues for a connected workspace so the SpineApp can show
// project issues and let users start AI sessions directly from an issue.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Workspaces;

namespace AgentOs.Modules.Integration;

/// <summary>A GitHub Issue as shown in the Projects → Issues view.</summary>
/// <param name="Number">Issue number (e.g. 42).</param>
/// <param name="Title">Issue title.</param>
/// <param name="State">State string: "Open" or "Closed".</param>
/// <param name="HtmlUrl">Link to the issue on GitHub.</param>
/// <param name="AssigneeLogin">Assignee GitHub login, or <c>null</c> if unassigned.</param>
/// <param name="Labels">Label names attached to the issue.</param>
public sealed record GitHubIssue(
    int Number,
    string Title,
    string State,
    string HtmlUrl,
    string? AssigneeLogin,
    System.Collections.Generic.IReadOnlyList<string> Labels);

/// <summary>Fetches issues from a GitHub repository using the workspace's stored PAT.</summary>
public interface IGitHubIssueService
{
    /// <summary>List open issues (excluding pull requests) for the given workspace, newest first.
    /// At most 50 issues are returned (pagination is a future feature).</summary>
    Task<IReadOnlyList<GitHubIssue>> ListOpenIssuesAsync(
        WorkspaceDescriptor workspace,
        CancellationToken cancellationToken = default);
}
