// M6 — default ISessionRunFeed: a per-tenant fan-out over bounded channels. Publish writes to every
// live subscriber's channel; the channel is bounded + DropOldest so a slow or abandoned subscriber can
// never block a run (the publisher's TryWrite always succeeds and is non-blocking). A subscriber's
// channel is removed when its enumeration is cancelled or disposed. Pure BCL (System.Threading.Channels)
// so it lives in Domain alongside the contract — no DI or runtime dependency.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;

namespace AgentOs.Domain.Sessions;

/// <inheritdoc cref="ISessionRunFeed" />
public sealed class InMemorySessionRunFeed : ISessionRunFeed
{
    // tenantId -> (subscriptionId -> channel). Concurrent at both levels for lock-free publish/subscribe.
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Channel<SessionRunEvent>>> _subscribers =
        new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void Publish(SessionRunEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        if (!_subscribers.TryGetValue(evt.TenantId, out var byId))
        {
            return;
        }

        foreach (var channel in byId.Values)
        {
            // Bounded + DropOldest → TryWrite never blocks and never fails; a lagging reader simply
            // loses the oldest queued events, never the publisher's forward progress.
            channel.Writer.TryWrite(evt);
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SessionRunEvent> Subscribe(
        string tenantId, [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var channel = Channel.CreateBounded<SessionRunEvent>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        var subscriptionId = Guid.NewGuid();
        var byId = _subscribers.GetOrAdd(tenantId, static _ => new ConcurrentDictionary<Guid, Channel<SessionRunEvent>>());
        byId[subscriptionId] = channel;

        try
        {
            await foreach (var evt in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                yield return evt;
            }
        }
        finally
        {
            byId.TryRemove(subscriptionId, out _);
            if (byId.IsEmpty)
            {
                // Best-effort prune of the now-empty tenant bucket. A concurrent subscribe may have just
                // re-added it; that is harmless (the next publish/subscribe re-creates as needed).
                _subscribers.TryRemove(tenantId, out _);
            }
        }
    }
}
