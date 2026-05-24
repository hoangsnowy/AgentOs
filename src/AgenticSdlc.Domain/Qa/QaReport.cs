// AgenticSdlc.Domain/Qa/QaReport.cs
// Phase 3 — Output of QaAgent — requirement-code-test consistency report.

using System.Collections.Generic;

namespace AgenticSdlc.Domain.Qa;

/// <summary>
/// QA report — evaluates consistency between <c>RequirementSpec</c>, <c>CodeArtifact</c>, <c>TestArtifact</c>.
/// The <see cref="IterationNeeded"/> field drives the <c>PipelineOrchestrator</c> loop.
/// </summary>
/// <param name="Score">Overall score [0.0, 1.0]. ≥ <see cref="QaThresholds.PassScore"/> → pass.</param>
/// <param name="IsConsistent">Overall consistency flag.</param>
/// <param name="IterationNeeded">Continue-loop flag. <c>true</c> = code/test needs regenerating.</param>
/// <param name="Issues">List of detected issues (requirement-vs-code drift, missing tests, ...).</param>
/// <param name="Recommendations">Recommendations for the next regeneration.</param>
/// <param name="Metrics">Agent metric.</param>
public sealed record QaReport(
    double Score,
    bool IsConsistent,
    bool IterationNeeded,
    IReadOnlyList<QaIssue> Issues,
    IReadOnlyList<string> Recommendations,
    AgentMetrics Metrics);

/// <summary>A single QA issue.</summary>
/// <param name="Severity">Severity (<c>Critical</c> / <c>Major</c> / <c>Minor</c>).</param>
/// <param name="Category">Category (<c>RequirementCoverage</c>, <c>TestCoverage</c>, <c>CodeQuality</c>, ...).</param>
/// <param name="Description">Detailed description.</param>
/// <param name="Location">Location (file path, line, requirement ID).</param>
public sealed record QaIssue(string Severity, string Category, string Description, string? Location = null);

/// <summary>Hardcoded QA thresholds for the thesis prototype.</summary>
public static class QaThresholds
{
    /// <summary>Minimum score to pass (thesis Section 2.4.5).</summary>
    public const double PassScore = 0.8;
}
