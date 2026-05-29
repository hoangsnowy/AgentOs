// Multi-key routing for the SDK LLM clients. Each provider can be configured with several API keys.
// This router hands out keys round-robin and, when a key is rate-limited (HTTP 429), puts it on a
// cooldown so subsequent calls fail over to the other keys. Singleton so cooldown state is shared.

using System;
using System.Collections.Generic;
using System.Linq;

namespace AgenticSdlc.Modules.Llm;

/// <summary>Round-robin API-key selector with per-key cooldown on rate-limit. Thread-safe.</summary>
public sealed class ApiKeyRouter
{
    public static readonly TimeSpan DefaultCooldown = TimeSpan.FromSeconds(60);

    private sealed class ProviderState
    {
        public int RoundRobin;
        public readonly Dictionary<string, DateTimeOffset> CooldownUntil = new(StringComparer.Ordinal);
    }

    private readonly Dictionary<string, ProviderState> _byProvider = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly TimeProvider _clock;

    public ApiKeyRouter(TimeProvider clock)
        => _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>Pick the next usable key for <paramref name="provider"/>, round-robin, skipping cooldowns.</summary>
    public string? Acquire(string provider, IReadOnlyList<string> keys)
    {
        if (keys is null || keys.Count == 0)
        {
            return null;
        }

        lock (_gate)
        {
            var state = GetState(provider);
            var now = _clock.GetUtcNow();

            for (var i = 0; i < keys.Count; i++)
            {
                var index = (state.RoundRobin + i) % keys.Count;
                var key = keys[index];
                if (!state.CooldownUntil.TryGetValue(key, out var until) || until <= now)
                {
                    state.RoundRobin = index + 1;
                    return key;
                }
            }

            return keys.OrderBy(k => state.CooldownUntil.TryGetValue(k, out var u) ? u : now).First();
        }
    }

    /// <summary>Mark <paramref name="key"/> rate-limited; skipped until cooldown elapses.</summary>
    public void Penalize(string provider, string key, TimeSpan? retryAfter = null)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        lock (_gate)
        {
            GetState(provider).CooldownUntil[key] = _clock.GetUtcNow() + (retryAfter ?? DefaultCooldown);
        }
    }

    /// <summary>Count of keys not currently on cooldown.</summary>
    public int AvailableCount(string provider, IReadOnlyList<string> keys)
    {
        if (keys is null || keys.Count == 0)
        {
            return 0;
        }

        lock (_gate)
        {
            var state = GetState(provider);
            var now = _clock.GetUtcNow();
            return keys.Count(k => !state.CooldownUntil.TryGetValue(k, out var until) || until <= now);
        }
    }

    private ProviderState GetState(string provider)
    {
        if (!_byProvider.TryGetValue(provider, out var state))
        {
            state = new ProviderState();
            _byProvider[provider] = state;
        }
        return state;
    }
}
