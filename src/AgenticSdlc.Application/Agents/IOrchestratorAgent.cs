// AgenticSdlc.Application/Agents/IOrchestratorAgent.cs
// Phase 3 — Contract cho PipelineOrchestrator.

using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Domain.Pipeline;

namespace AgenticSdlc.Application.Agents;

/// <summary>
/// Điều phối 4 specialist agent + QA loop. Mục 2.4.0 luận văn.
/// </summary>
public interface IOrchestratorAgent
{
    /// <summary>Chạy pipeline end-to-end.</summary>
    /// <param name="story">User story đầu vào.</param>
    /// <param name="cancellationToken">Token huỷ.</param>
    /// <returns><see cref="PipelineResult"/> bundle artefact cuối + history QA.</returns>
    Task<PipelineResult> RunAsync(UserStory story, CancellationToken cancellationToken = default);
}
