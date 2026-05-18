// AgenticSdlc.Domain/Pipeline/PipelineResult.cs
// Phase 3 — Output của PipelineOrchestrator — bundle toàn bộ artefact + history QA loop.

using System.Collections.Generic;
using AgenticSdlc.Domain.Code;
using AgenticSdlc.Domain.Qa;
using AgenticSdlc.Domain.Requirements;
using AgenticSdlc.Domain.Testing;

namespace AgenticSdlc.Domain.Pipeline;

/// <summary>
/// Kết quả cuối pipeline 5-tác-tử. Bao gồm artefact cuối + history QA loop.
/// </summary>
/// <param name="UserStory">User story đầu vào.</param>
/// <param name="Spec">Requirement spec đã sinh.</param>
/// <param name="Code">Code artifact cuối (lần iteration cuối).</param>
/// <param name="Tests">Test artifact cuối.</param>
/// <param name="QaHistory">Toàn bộ QA report theo thứ tự iteration (index 0 = lần đầu).</param>
/// <param name="Status">Trạng thái cuối.</param>
/// <param name="TotalMetrics">Tổng cost / token / latency của tất cả agent call.</param>
public sealed record PipelineResult(
    UserStory UserStory,
    RequirementSpec Spec,
    CodeArtifact Code,
    TestArtifact Tests,
    IReadOnlyList<QaReport> QaHistory,
    PipelineStatus Status,
    AgentMetrics TotalMetrics)
{
    /// <summary>Số iteration QA đã chạy (chiều dài <see cref="QaHistory"/>).</summary>
    public int IterationCount => QaHistory?.Count ?? 0;
}

/// <summary>Trạng thái cuối pipeline.</summary>
public enum PipelineStatus
{
    /// <summary>QA pass trong giới hạn iteration.</summary>
    Done,
    /// <summary>Đã chạm <c>NMax</c> mà QA vẫn fail.</summary>
    MaxIterationReached,
    /// <summary>Lỗi nghiêm trọng (LLM exception, malformed output, ...).</summary>
    Failed,
}
