// AgenticSdlc.Application/Pipeline/PipelineOptions.cs
// Phase 3 — Options binding for the "Pipeline" section in appsettings.

namespace AgenticSdlc.Modules.Pipeline.Pipeline;

/// <summary>
/// Pipeline orchestrator configuration.
/// </summary>
public sealed class PipelineOptions
{
    /// <summary>Section name.</summary>
    public const string SectionName = "Pipeline";

    /// <summary>Maximum number of QA iterations if the user does not override via <c>UserStory.NMax</c>.</summary>
    public int MaxIterations { get; set; } = 3;

    /// <summary>Enables the human-in-the-loop checkpoint (not yet implemented, Phase 4).</summary>
    public bool EnableHumanInTheLoop { get; set; }

    /// <summary>
    /// Orchestration engine: <c>"Classic"</c> (in-process loop, default) or <c>"Workflow"</c>
    /// (Microsoft Agent Framework Workflows graph). platform-v2 / feat/sdk-maf.
    /// </summary>
    public string Engine { get; set; } = "Classic";
}
