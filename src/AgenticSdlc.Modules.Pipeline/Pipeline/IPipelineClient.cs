// AgenticSdlc.Application/Pipeline/IPipelineClient.cs
// Phase 8 — Port the Web depends on to run the 5-agent pipeline.
// Two impls live in Infrastructure:
//   * InProcessPipelineClient — runs IOrchestratorAgent inside the Web circuit (Phase 7 behavior).
//   * HttpPipelineClient      — POSTs /pipeline/stream over HTTP to a remote API (Phase 8 default).
// The two share the same IAsyncEnumerable wire contract, so the UI is identical regardless of host.

using System.Collections.Generic;
using System.Threading;
using AgenticSdlc.Domain.Pipeline;
using AgenticSdlc.Domain.Requirements;

namespace AgenticSdlc.Modules.Pipeline.Pipeline;

/// <summary>
/// Run the 5-agent pipeline and stream progress + result back to the caller. The stream is an
/// ordered <see cref="IAsyncEnumerable{T}"/> of <see cref="PipelineStreamEvent"/> instances.
/// </summary>
public interface IPipelineClient
{
    /// <summary>
    /// Starts the pipeline and yields events as they are produced. Completes when the run
    /// terminates (Result or Error envelope is the last event yielded).
    /// </summary>
    /// <param name="story">The user story to run.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    IAsyncEnumerable<PipelineStreamEvent> StreamAsync(UserStory story, CancellationToken cancellationToken = default);
}
