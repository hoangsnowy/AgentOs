// AgenticSdlc.Infrastructure/Pipeline/NullPipelineProgressSink.cs
// Phase 7 — No-op impl of IPipelineProgressSink. Registered as the default so the API + tests
// keep their behavior (ignoring progress). Blazor overrides it with a scoped version.

using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Modules.Pipeline.Pipeline;
using AgenticSdlc.Domain.Pipeline;

namespace AgenticSdlc.Modules.Pipeline.Pipeline;

/// <summary>
/// Empty implementation — swallows all progress events. Used for hosts that do not need realtime
/// (API, unit tests). Also the fallback when the orchestrator is not injected with a sink.
/// </summary>
public sealed class NullPipelineProgressSink : IPipelineProgressSink
{
    /// <summary>Shared instance (stateless, thread-safe).</summary>
    public static NullPipelineProgressSink Instance { get; } = new();

    /// <inheritdoc />
    public ValueTask ReportAsync(PipelineProgressEvent progress, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
}
