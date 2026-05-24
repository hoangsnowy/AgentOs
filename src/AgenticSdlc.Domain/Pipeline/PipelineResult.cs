// AgenticSdlc.Domain/Pipeline/PipelineResult.cs
// Phase 3 — Output of the PipelineOrchestrator — bundles all final artifacts + the QA loop history.

using System.Collections.Generic;
using AgenticSdlc.Domain.Code;
using AgenticSdlc.Domain.Qa;
using AgenticSdlc.Domain.Requirements;
using AgenticSdlc.Domain.Testing;

namespace AgenticSdlc.Domain.Pipeline;

/// <summary>
/// Final result of the 5-agent pipeline. Includes the final artifacts + the QA loop history.
/// </summary>
/// <param name="UserStory">The input user story.</param>
/// <param name="Spec">The generated requirement spec.</param>
/// <param name="Code">The final code artifact (from the last iteration).</param>
/// <param name="Tests">The final test artifact.</param>
/// <param name="QaHistory">All QA reports in iteration order (index 0 = first iteration).</param>
/// <param name="Status">Final status.</param>
/// <param name="TotalMetrics">Total cost / token / latency across all agent calls.</param>
public sealed record PipelineResult(
    UserStory UserStory,
    RequirementSpec Spec,
    CodeArtifact Code,
    TestArtifact Tests,
    IReadOnlyList<QaReport> QaHistory,
    PipelineStatus Status,
    AgentMetrics TotalMetrics)
{
    /// <summary>Number of QA iterations that ran (length of <see cref="QaHistory"/>).</summary>
    public int IterationCount => QaHistory?.Count ?? 0;
}

/// <summary>Final pipeline status.</summary>
public enum PipelineStatus
{
    /// <summary>QA passed within the iteration limit.</summary>
    Done,
    /// <summary>Reached <c>NMax</c> while QA still failed.</summary>
    MaxIterationReached,
    /// <summary>Critical error (LLM exception, malformed output, ...).</summary>
    Failed,
}
