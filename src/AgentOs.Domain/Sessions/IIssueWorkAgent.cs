// M5 — contract for the issue-work agent: given a GitHub issue + workspace coordinates, run the
// agentic LLM loop (using runner_shell) to implement a fix, push a branch, and return the branch
// name for the caller to open a PR against.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace AgentOs.Domain.Sessions;

/// <summary>Request to the issue-work agent.</summary>
/// <param name="SessionId">Correlating session row for evidence / logging.</param>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="MemberId">User who owns the session (used for runner targeting).</param>
/// <param name="WorkspaceOwner">GitHub owner / org.</param>
/// <param name="WorkspaceRepo">Repository name.</param>
/// <param name="WorkspaceDefaultBranch">Base branch the fix branch is cut from.</param>
/// <param name="IssueNumber">GitHub issue number.</param>
/// <param name="IssueTitle">Issue title (short context for the LLM).</param>
/// <param name="IssueBody">Issue body / description (full context for the LLM).</param>
public sealed record IssueWorkRequest(
    Guid SessionId,
    string TenantId,
    string MemberId,
    string WorkspaceOwner,
    string WorkspaceRepo,
    string WorkspaceDefaultBranch,
    int IssueNumber,
    string IssueTitle,
    string IssueBody);

/// <summary>Result from the issue-work agent.</summary>
/// <param name="Ok">True when the agent pushed a branch successfully.</param>
/// <param name="BranchName">Pushed branch name (e.g. <c>issue-42-ai-fix</c>). Empty on failure.</param>
/// <param name="Summary">One-sentence description of what was changed.</param>
/// <param name="Error">Failure reason; null when <see cref="Ok"/> is true.</param>
public sealed record IssueWorkResult(bool Ok, string BranchName, string Summary, string? Error);

/// <summary>Runs the agentic LLM loop for a GitHub issue against the paired dev-machine runner.</summary>
public interface IIssueWorkAgent
{
    Task<IssueWorkResult> RunAsync(IssueWorkRequest request, CancellationToken ct = default);
}
