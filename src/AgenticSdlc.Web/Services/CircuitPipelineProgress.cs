// AgenticSdlc.Web/Services/CircuitPipelineProgress.cs
// Phase 7 — Per-Blazor-circuit progress sink. The orchestrator (resolved in the same
// circuit scope) reports events here; the Studio page registers a Listener to re-render in realtime.

using System;
using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Application.Pipeline;
using AgenticSdlc.Domain.Pipeline;

namespace AgenticSdlc.Web.Services;

/// <summary>
/// A circuit-scoped <see cref="IPipelineProgressSink"/> implementation. It never touches the UI itself —
/// it only forwards events to the <see cref="Listener"/> the component installs (the component is
/// responsible for calling <c>InvokeAsync(StateHasChanged)</c> to render on Blazor's synchronization context).
/// </summary>
public sealed class CircuitPipelineProgress : IPipelineProgressSink
{
    /// <summary>Callback registered by the component. <c>null</c> ⇒ ignored (no listener).</summary>
    public Func<PipelineProgressEvent, Task>? Listener { get; set; }

    /// <inheritdoc />
    public async ValueTask ReportAsync(PipelineProgressEvent progress, CancellationToken cancellationToken = default)
    {
        var listener = Listener;
        if (listener is not null)
        {
            await listener(progress).ConfigureAwait(false);
        }
    }
}
