// Chain-of-Responsibility ILlmClient: tries each provider in order, advancing to the next when one
// throws LlmException — its provider-level failure signal (every key rate-limited, or no key configured).
// This is layered ABOVE each PooledChatLlmClient's intra-provider key-pool failover: the pooled client
// exhausts its own keys first and only then throws LlmException, which is what triggers the jump to the
// next provider here. Composed by LlmClientFactory from the Llm:Fallbacks config; absent config means a
// bare single client is returned instead, so there is zero overhead when no fallback is declared.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Llm;
using Microsoft.Extensions.Logging;

namespace AgentOs.Modules.Llm;

internal sealed class FailoverLlmClient : ILlmClient
{
    private readonly IReadOnlyList<ILlmClient> _chain;
    private readonly ILogger _logger;

    public FailoverLlmClient(IReadOnlyList<ILlmClient> chain, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(chain);
        if (chain.Count == 0)
        {
            throw new ArgumentException("Failover chain must have at least one client.", nameof(chain));
        }

        _chain = chain;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string Provider => _chain[0].Provider;

    /// <inheritdoc />
    public async Task<LlmResponse> SendAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < _chain.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await _chain[i].SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            // Narrow by design: only a provider-level LlmException triggers failover, and only while a
            // next provider exists. On the last link the guard is false, so its exception propagates.
            catch (LlmException ex) when (i + 1 < _chain.Count)
            {
                _logger.LogWarning(
                    "[failover] provider '{Failed}' unavailable ({Reason}) — falling over to '{Next}'.",
                    _chain[i].Provider, ex.Message, _chain[i + 1].Provider);
            }
        }

        // Unreachable: the final link either returns or lets its LlmException propagate (guard is false).
        throw new LlmException("Failover chain exhausted with no successful provider.", Provider);
    }
}
