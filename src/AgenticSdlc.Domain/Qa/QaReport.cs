// AgenticSdlc.Domain/Qa/QaReport.cs
// Phase 3 — Output của QaAgent — báo cáo nhất quán requirement-code-test.

using System.Collections.Generic;

namespace AgenticSdlc.Domain.Qa;

/// <summary>
/// Báo cáo QA — đánh giá nhất quán giữa <c>RequirementSpec</c>, <c>CodeArtifact</c>, <c>TestArtifact</c>.
/// Field <see cref="IterationNeeded"/> drive vòng lặp của <c>PipelineOrchestrator</c>.
/// </summary>
/// <param name="Score">Điểm tổng [0.0, 1.0]. ≥ <see cref="QaThresholds.PassScore"/> → pass.</param>
/// <param name="IsConsistent">Cờ nhất quán tổng thể.</param>
/// <param name="IterationNeeded">Cờ tiếp tục loop. <c>true</c> = cần regenerate code/test.</param>
/// <param name="Issues">Danh sách vấn đề phát hiện (drift requirement-vs-code, test thiếu, ...).</param>
/// <param name="Recommendations">Khuyến nghị cho lần regenerate kế tiếp.</param>
/// <param name="Metrics">Metric agent.</param>
public sealed record QaReport(
    double Score,
    bool IsConsistent,
    bool IterationNeeded,
    IReadOnlyList<QaIssue> Issues,
    IReadOnlyList<string> Recommendations,
    AgentMetrics Metrics);

/// <summary>1 vấn đề QA.</summary>
/// <param name="Severity">Mức độ (<c>Critical</c> / <c>Major</c> / <c>Minor</c>).</param>
/// <param name="Category">Phân loại (<c>RequirementCoverage</c>, <c>TestCoverage</c>, <c>CodeQuality</c>, ...).</param>
/// <param name="Description">Mô tả chi tiết.</param>
/// <param name="Location">Vị trí (file path, line, requirement ID).</param>
public sealed record QaIssue(string Severity, string Category, string Description, string? Location = null);

/// <summary>Ngưỡng QA hardcode cho prototype luận văn.</summary>
public static class QaThresholds
{
    /// <summary>Điểm tối thiểu để pass (Mục 2.4.5 luận văn).</summary>
    public const double PassScore = 0.8;
}
