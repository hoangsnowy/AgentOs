// M5 — domain seam for creating a PR from an already-pushed branch. The runner pushes the branch;
// the server creates the PR via the source-control API. Implemented by Integration module.

using System.Threading;
using System.Threading.Tasks;

namespace AgentOs.Domain.Workspaces;

/// <summary>Result of <see cref="IPrCreationService.CreateFromBranchAsync"/>.</summary>
/// <param name="Ok">True when the PR was created successfully.</param>
/// <param name="Number">PR number assigned by the provider. Null on failure.</param>
/// <param name="HtmlUrl">Browser URL of the created PR. Null on failure.</param>
/// <param name="Error">Failure reason; null when <see cref="Ok"/> is true.</param>
public sealed record PrCreationResult(bool Ok, int? Number, string? HtmlUrl, string? Error);

/// <summary>
/// Creates a pull request from an already-pushed branch in the workspace's repository.
/// PAT comes from <see cref="WorkspaceDescriptor.AccessToken"/> — never from tenant-global settings.
/// </summary>
public interface IPrCreationService
{
    Task<PrCreationResult> CreateFromBranchAsync(
        WorkspaceDescriptor workspace,
        string branch,
        string title,
        string body,
        CancellationToken ct = default);
}
