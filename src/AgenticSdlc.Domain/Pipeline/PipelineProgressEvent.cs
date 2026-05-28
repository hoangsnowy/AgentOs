// AgenticSdlc.Domain/Pipeline/PipelineProgressEvent.cs
// Phase 7 — Progress event emitted by the PipelineOrchestrator while running.
// Lets the presentation layer (Blazor) display each step of the QA loop in real time.

namespace AgenticSdlc.Domain.Pipeline;

/// <summary>
/// A progress milestone of the 5-agent pipeline. The orchestrator emits this event at
/// the start/end of each step (Requirement → Coding/Testing/Qa loop → aggregate) so the
/// UI can build a real-time timeline. The event is immutable and serializable.
/// </summary>
/// <param name="Stage">The step currently running.</param>
/// <param name="Phase">Phase of the step (started / completed / failed).</param>
/// <param name="Iteration">QA loop iteration number (<c>0</c> for the pre-loop Requirement step; <c>1..N</c> inside the loop).</param>
/// <param name="MaxIterations">The clamped iteration limit — used to display "iteration N/Max".</param>
/// <param name="Message">A short, user-ready description.</param>
/// <param name="QaScore">QA score <c>[0.0, 1.0]</c> — present only at <see cref="PipelineStage.Qa"/> phase <see cref="PipelinePhase.Completed"/>.</param>
/// <param name="QaConsistent">QA consistency flag — present only when the QA step completes.</param>
/// <param name="Metrics">Metric of the step just completed (token / cost / latency); <c>null</c> at the start phase.</param>
/// <param name="TimestampUtc">Timestamp when the event was emitted (UTC).</param>
public sealed record PipelineProgressEvent(
    PipelineStage Stage,
    PipelinePhase Phase,
    int Iteration,
    int MaxIterations,
    string Message,
    double? QaScore = null,
    bool? QaConsistent = null,
    AgentMetrics? Metrics = null,
    System.DateTime? TimestampUtc = null)
{
    /// <summary>Emission time — defaults to <see cref="System.DateTime.UtcNow"/> when not supplied.</summary>
    public System.DateTime OccurredAtUtc => TimestampUtc ?? System.DateTime.UtcNow;
}

/// <summary>A step in the 5-agent pipeline.</summary>
public enum PipelineStage
{
    /// <summary>KC1 — Requirement Agent analyzes the user story.</summary>
    Requirement,

    /// <summary>KC2 — Coding Agent generates source code.</summary>
    Coding,

    /// <summary>KC3 — Testing Agent generates test cases.</summary>
    Testing,

    /// <summary>KC5 — QA Agent evaluates consistency.</summary>
    Qa,

    /// <summary>Aggregate metrics + finalize the pipeline result.</summary>
    Aggregate,
}

/// <summary>Phase of a step.</summary>
public enum PipelinePhase
{
    /// <summary>The step just started.</summary>
    Started,

    /// <summary>The step completed successfully.</summary>
    Completed,

    /// <summary>The step failed (LLM exception, malformed output, ...).</summary>
    Failed,
}
