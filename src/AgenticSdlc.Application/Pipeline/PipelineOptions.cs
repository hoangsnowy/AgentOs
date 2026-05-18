// AgenticSdlc.Application/Pipeline/PipelineOptions.cs
// Phase 3 — Options binding section "Pipeline" trong appsettings.

namespace AgenticSdlc.Application.Pipeline;

/// <summary>
/// Cấu hình pipeline orchestrator.
/// </summary>
public sealed class PipelineOptions
{
    /// <summary>Section name.</summary>
    public const string SectionName = "Pipeline";

    /// <summary>Số iteration QA tối đa nếu user không override qua <c>UserStory.NMax</c>.</summary>
    public int MaxIterations { get; set; } = 3;

    /// <summary>Bật human-in-the-loop checkpoint (chưa implement Phase 4).</summary>
    public bool EnableHumanInTheLoop { get; set; }
}
