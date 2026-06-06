// M6 perf — takes evidence writes OFF the tool-call critical path. The gateway awaits
// IToolInvocationLog.AppendAsync after every governed tool call (DefaultToolGateway), and the durable
// EF sink does a DI-scope + SaveChanges per call — so a session run's agentic loop paid N serial DB
// round-trips on its wall-time. This decorator makes AppendAsync a non-blocking enqueue and drains the
// queue to the inner sink on one background loop. The IToolInvocationLog contract is already
// best-effort ("failures must NOT bubble up"), so dropping under flood / eventual persistence is in
// spec. Pure BCL channels (same pattern as InMemorySessionRunFeed) — no hosting dependency.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using AgentOs.Domain.Tools;

namespace AgentOs.Modules.Tools.Evidence;

/// <summary>Non-blocking buffering decorator over an inner <see cref="IToolInvocationLog"/>.</summary>
internal sealed class BufferedToolInvocationLog : IToolInvocationLog, IAsyncDisposable
{
    private readonly IToolInvocationLog _inner;
    private readonly Channel<ToolInvocationEvidence> _channel;
    private CancellationTokenSource? _cts;
    private Task? _drainLoop;

    public BufferedToolInvocationLog(IToolInvocationLog inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        // Bounded + DropOldest: TryWrite never blocks the publisher and never grows memory unbounded.
        // Evidence is best-effort, so shedding the oldest queued row under a flood is acceptable.
        _channel = Channel.CreateBounded<ToolInvocationEvidence>(new BoundedChannelOptions(4096)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <summary>Enqueue + return immediately. Off the tool-call critical path; never throws.</summary>
    public Task AppendAsync(ToolInvocationEvidence entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _channel.Writer.TryWrite(entry);
        return Task.CompletedTask;
    }

    /// <summary>Reads delegate to the inner sink (already-persisted rows; queued rows appear once drained).</summary>
    public Task<IReadOnlyList<ToolInvocationEvidence>> ListRecentAsync(
        string tenantId, int limit = 50, CancellationToken cancellationToken = default) =>
        _inner.ListRecentAsync(tenantId, limit, cancellationToken);

    /// <summary>Start the single background drain loop. Called once at module init (NOT in tests, which
    /// drive <see cref="DrainPendingAsync"/> as the sole reader for determinism).</summary>
    public void StartDraining()
    {
        if (_drainLoop is not null)
        {
            return;
        }
        _cts = new CancellationTokenSource();
        _drainLoop = Task.Run(() => DrainLoopAsync(_cts.Token));
    }

    private async Task DrainLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var entry in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                // Don't cancel a write that's already started — let queued evidence finish persisting.
                await _inner.AppendAsync(entry, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    /// <summary>Drain everything currently queued into the inner sink synchronously. Test-only: safe only
    /// when the background loop is NOT running (otherwise two readers race the single-reader channel).</summary>
    internal async Task DrainPendingAsync()
    {
        while (_channel.Reader.TryRead(out var entry))
        {
            await _inner.AppendAsync(entry, CancellationToken.None).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Complete the writer so the drain loop persists what's left, then await it (bounded).
        _channel.Writer.TryComplete();
        if (_drainLoop is not null)
        {
            try { await _drainLoop.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            // Best-effort flush on shutdown — a timeout or a final persistence failure must not block dispose.
            catch (TimeoutException ex) { _ = ex.Message; }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException ex) { _ = ex.Message; }
            catch (System.Data.Common.DbException ex) { _ = ex.Message; }
            catch (ObjectDisposedException ex) { _ = ex.Message; }
            catch (InvalidOperationException ex) { _ = ex.Message; }
        }
        _cts?.Dispose();
    }
}
