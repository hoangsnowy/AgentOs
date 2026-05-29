// AgenticSdlc.Application/Integration/IGitHubPrService.cs
// Opens a GitHub pull request that contains the generated code + tests from a pipeline run.

using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Domain.Pipeline;

namespace AgenticSdlc.Modules.Integration;

/// <summary>Pushes a <see cref="PipelineResult"/>'s generated code + tests to a target GitHub repo and opens a PR.</summary>
public interface IGitHubPrService
{
    /// <summary>
    /// Create a new branch (timestamped) from the base branch, commit every file in
    /// <see cref="PipelineResult.Code"/> and <see cref="PipelineResult.Tests"/>, and open a PR.
    /// PAT and target repo are read from <c>IRuntimeOverrides</c> (set via the Settings page).
    /// </summary>
    Task<GitHubPrResult> OpenPrAsync(PipelineResult result, string title, string body, CancellationToken ct);
}

/// <summary>Result of <see cref="IGitHubPrService.OpenPrAsync"/>.</summary>
/// <param name="Number">PR number assigned by GitHub.</param>
/// <param name="HtmlUrl">Browser URL of the PR.</param>
/// <param name="Branch">Name of the branch the PR is opened from.</param>
public sealed record GitHubPrResult(int Number, string HtmlUrl, string Branch);
