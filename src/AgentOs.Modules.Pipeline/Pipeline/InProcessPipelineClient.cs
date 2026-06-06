// AgentOs.Infrastructure/Pipeline/InProcessPipelineClient.cs
// Phase 8 — In-process IPipelineClient: runs the orchestrator on the same node as Web.
// Used when Api:BaseUrl is unset (single-process dev mode). Bridges progress sink → channel
// → IAsyncEnumerable so the wire shape matches the HTTP path exactly.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using AgentOs.Modules.Pipeline.Agents;
using AgentOs.Modules.Pipeline.Pipeline;
using AgentOs.Domain.Pipeline;
using AgentOs.Domain.Requirements;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentOs.Modules.Pipeline.Pipeline;

/// <summary>
/// Runs the 5-agent pipeline inside the current process. A per-call DI scope is created so the
/// scope's <see cref="MutableSinkHolder"/> can be redirected at our channel writer for the duration
/// of one run.
/// </summary>
public sealed class InProcessPipelineClient : IPipelineClient
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InProcessPipelineClient> _logger;

    /// <summary>Construct with an <see cref="IServiceScopeFactory"/> for per-run scoping.</summary>
    public InProcessPipelineClient(IServiceScopeFactory scopeFactory, ILogger<InProcessPipelineClient> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<PipelineStreamEvent> StreamAsync(
        UserStory story,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(story);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var channel = Channel.CreateUnbounded<PipelineProgressEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        // Route the orchestrator's sink calls at our channel for the duration of this run.
        var holder = scope.ServiceProvider.GetRequiredService<MutableSinkHolder>();
        holder.SetSink(new ChannelProgressSink(channel));
        var orchestrator = scope.ServiceProvider.GetRequiredService<IOrchestratorAgent>();

        var runTask = Task.Run<(PipelineResult? Result, Exception? Error)>(async () =>
        {
            try
            {
                var result = await orchestrator.RunAsync(story, cancellationToken).ConfigureAwait(false);
                return (result, null);
            }
            catch (Exception ex)
            {
                return (null, ex);
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, cancellationToken);

        await foreach (var progress in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return new PipelineStreamEvent(PipelineStreamEventKind.Progress, Progress: progress);
        }

        var (result, error) = await runTask.ConfigureAwait(false);
        if (error is not null)
        {
            _logger.LogError(error, "InProcess pipeline run failed");
            yield return new PipelineStreamEvent(PipelineStreamEventKind.Error, Error: error.Message);
        }
        else if (result is not null)
        {
            yield return new PipelineStreamEvent(PipelineStreamEventKind.Result, Result: result);
        }
    }
}

/// <summary>
/// Scoped holder for an <see cref="IPipelineProgressSink"/> instance. Registered as the
/// <see cref="IPipelineProgressSink"/> implementation so anyone in the scope (e.g. the orchestrator)
/// reporting progress lands in whatever sink the holder is currently pointing at. The endpoint
/// (Api SSE) or the in-process client swap the inner sink for the duration of one run.
/// </summary>
public sealed class MutableSinkHolder : IPipelineProgressSink
{
    private IPipelineProgressSink _inner = NullPipelineProgressSink.Instance;

    /// <summary>Replace the current sink with <paramref name="sink"/> for the rest of the scope's life.</summary>
    public void SetSink(IPipelineProgressSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _inner = sink;
    }

    /// <inheritdoc />
    public ValueTask ReportAsync(PipelineProgressEvent progress, CancellationToken cancellationToken = default)
        => _inner.ReportAsync(progress, cancellationToken);
}

/// <summary>Progress sink that pushes events into a Channel for IAsyncEnumerable consumption.</summary>
internal sealed class ChannelProgressSink : IPipelineProgressSink
{
    private readonly Channel<PipelineProgressEvent> _channel;
    public ChannelProgressSink(Channel<PipelineProgressEvent> channel) => _channel = channel;
    public ValueTask ReportAsync(PipelineProgressEvent progress, CancellationToken cancellationToken = default)
    {
        _channel.Writer.TryWrite(progress);
        return ValueTask.CompletedTask;
    }
}
