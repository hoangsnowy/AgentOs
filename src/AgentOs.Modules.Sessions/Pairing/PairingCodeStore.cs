// Short-lived, single-use exchange codes for the VS Code browser-pairing flow. The browser redirect to
// the editor carries only the CODE (never the token); the extension exchanges it once, over a direct
// POST, for the actual runner credentials. In-memory + bounded by TTL — a pairing is completed within
// seconds, so there is nothing durable to persist.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;

namespace AgentOs.Modules.Sessions.Pairing;

/// <summary>What a one-time pairing code redeems to — everything the runner needs to connect.</summary>
internal sealed record PairingPayload(Guid RunnerId, string Token, string HubUrl);

/// <summary>Issues + redeems one-time, short-lived pairing codes.</summary>
internal interface IPairingCodeStore
{
    /// <summary>Stash a payload and return a one-time code valid for <see cref="PairingCodeStore.Ttl"/>.</summary>
    string Stash(PairingPayload payload);

    /// <summary>Redeem and remove a code. Null when the code is unknown, already used, or expired.</summary>
    PairingPayload? Redeem(string code);
}

/// <inheritdoc />
internal sealed class PairingCodeStore : IPairingCodeStore
{
    /// <summary>How long a code stays valid — the extension exchanges it immediately after the redirect.</summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, (PairingPayload Payload, DateTimeOffset ExpiresAt)> _codes =
        new(StringComparer.Ordinal);

    public PairingCodeStore(TimeProvider clock) => _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <inheritdoc />
    public string Stash(PairingPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        Prune();
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        _codes[code] = (payload, _clock.GetUtcNow().Add(Ttl));
        return code;
    }

    /// <inheritdoc />
    public PairingPayload? Redeem(string code)
    {
        if (string.IsNullOrEmpty(code) || !_codes.TryRemove(code, out var entry))
        {
            return null;
        }
        return entry.ExpiresAt > _clock.GetUtcNow() ? entry.Payload : null;
    }

    // Drop expired codes opportunistically on each Stash so the dictionary can't grow unbounded from
    // abandoned pairings.
    private void Prune()
    {
        var now = _clock.GetUtcNow();
        foreach (var kvp in _codes.Where(kvp => kvp.Value.ExpiresAt <= now))
        {
            _codes.TryRemove(kvp.Key, out _);
        }
    }
}
