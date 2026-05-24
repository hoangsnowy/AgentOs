// AgenticSdlc.Application/Agents/IOrchestratorAgent.cs
// Phase 3 — Contract for the PipelineOrchestrator.

using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Domain.Pipeline;

namespace AgenticSdlc.Application.Agents;

/// <summary>
/// Orchestrates the 4 specialist agents + the QA loop. Thesis Section 2.4.0.
/// </summary>
public interface IOrchestratorAgent
{
    /// <summary>Runs the pipeline end-to-end.</summary>
    /// <param name="story">The input user story.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see cref="PipelineResult"/> bundling the final artifacts + QA history.</returns>
    Task<PipelineResult> RunAsync(UserStory story, CancellationToken cancellationToken = default);
}
