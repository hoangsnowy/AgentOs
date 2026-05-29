// AgenticSdlc.Domain/Pipeline/PipelineStreamEvent.cs
// Phase 8 — Wire envelope for streaming a pipeline run from API to Web.
// IAsyncEnumerable<PipelineStreamEvent> is what IPipelineClient hands the UI: a sequence of
// Progress events terminated by a single Result or Error envelope.

namespace AgenticSdlc.Domain.Pipeline;

/// <summary>
/// One frame of a streamed pipeline run. The stream is an ordered sequence of
/// <see cref="PipelineStreamEventKind.Progress"/> events followed by exactly one
/// <see cref="PipelineStreamEventKind.Result"/> or <see cref="PipelineStreamEventKind.Error"/>
/// envelope marking the end of the run.
/// </summary>
/// <param name="Kind">Discriminator for the payload.</param>
/// <param name="Progress">Set when <see cref="Kind"/> is <see cref="PipelineStreamEventKind.Progress"/>.</param>
/// <param name="Result">Set when <see cref="Kind"/> is <see cref="PipelineStreamEventKind.Result"/>.</param>
/// <param name="Error">Set when <see cref="Kind"/> is <see cref="PipelineStreamEventKind.Error"/>.</param>
public sealed record PipelineStreamEvent(
    PipelineStreamEventKind Kind,
    PipelineProgressEvent? Progress = null,
    PipelineResult? Result = null,
    string? Error = null);

/// <summary>Discriminator on <see cref="PipelineStreamEvent"/>.</summary>
public enum PipelineStreamEventKind
{
    /// <summary>A milestone of the run — start/complete of one stage.</summary>
    Progress,

    /// <summary>The final result envelope. Always the last event of a successful stream.</summary>
    Result,

    /// <summary>An error envelope. Always the last event of a failed stream.</summary>
    Error,
}
