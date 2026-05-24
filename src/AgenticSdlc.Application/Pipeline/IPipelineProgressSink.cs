// AgenticSdlc.Application/Pipeline/IPipelineProgressSink.cs
// Phase 7 — Progress-emission port. The orchestrator depends on this abstraction; the
// real impl is provided by the presentation layer (Blazor), or NullPipelineProgressSink (no-op).

using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Domain.Pipeline;

namespace AgenticSdlc.Application.Pipeline;

/// <summary>
/// The receiver for <see cref="PipelineProgressEvent"/> emitted by the <c>PipelineOrchestrator</c>.
/// Decouples orchestration from the display transport: the API/tests use a no-op version,
/// Blazor uses a version that pushes events down to the circuit for real-time rendering.
/// </summary>
public interface IPipelineProgressSink
{
    /// <summary>Reports a progress milestone. The impl must not throw and break the pipeline.</summary>
    /// <param name="progress">The progress event.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask ReportAsync(PipelineProgressEvent progress, CancellationToken cancellationToken = default);
}
