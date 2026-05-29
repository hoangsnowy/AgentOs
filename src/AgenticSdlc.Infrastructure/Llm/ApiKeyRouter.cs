// AgenticSdlc.Infrastructure/Llm/ApiKeyRouter.cs
// Multi-key routing for the HTTP LLM clients. Each provider can be configured with several API keys
// (Llm:Claude:ApiKeys, Llm:AzureOpenAi:ApiKeys). This router hands out keys round-robin and, when a key
// is rate-limited (HTTP 429), puts it on a cooldown so subsequent calls fail over to the other keys
// until its limit window passes. Registered as a singleton so cooldown state is shared across the
// (transient) client instances.

using System;
using System.Collections.Generic;
using System.Linq;

namespace AgenticSdlc.Infrastructure.Llm;

/// <summary>
/// Round-robin API-key selector with per-key cooldown on rate-limit. Thread-safe; one shared singleton
/// serves every provider (state is partitioned by provider name; cooldowns are keyed by the key string).
/// </summary>
public sealed class ApiKeyRouter
{
    /// <summary>Cooldown applied to a key on 429 when the provider sends no <c>Retry-After</c>.</summary>
    public static readonly TimeSpan DefaultCooldown = TimeSpan.FromSeconds(60);

    private sealed class ProviderState
    {
        public int RoundRobin;
        public readonly Dictionary<string, DateTimeOffset> CooldownUntil = new(StringComparer.Ordinal);
    }

    private readonly Dictionary<string, ProviderState> _byProvider = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly TimeProvider _clock;

    /// <summary>Creates the router. <paramref name="clock"/> drives cooldown timing (testable).</summary>
    public ApiKeyRouter(TimeProvider clock)
        => _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>
    /// Pick the next usable key for <paramref name="provider"/> from <paramref name="keys"/>, round-robin,
    /// skipping any key still on cooldown. If every key is cooling, returns the one whose cooldown expires
    /// soonest (a best-effort attempt rather than failing outright). Returns <c>null</c> only when
    /// <paramref name="keys"/> is empty.
    /// </summary>
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

            // All keys are cooling — return the soonest-to-recover so the caller still makes an attempt.
            return keys.OrderBy(k => state.CooldownUntil.TryGetValue(k, out var u) ? u : now).First();
        }
    }

    /// <summary>
    /// Mark <paramref name="key"/> rate-limited; it is skipped by <see cref="Acquire"/> until the cooldown
    /// (the provider's <c>Retry-After</c>, or <see cref="DefaultCooldown"/>) elapses.
    /// </summary>
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

    /// <summary>Count of keys not currently on cooldown — for logging / health.</summary>
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
