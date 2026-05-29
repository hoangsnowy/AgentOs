// SDK-based provider client: a keyed pool of Microsoft.Extensions.AI IChatClient instances (one per
// API key) selected by ApiKeyRouter, with round-robin + rate-limit (429) failover. Provider-agnostic
// — the key->IChatClient factory and the "is this a rate-limit error" predicate are injected, so the
// same class serves Claude (Anthropic.SDK) and Azure OpenAI (Azure.AI.OpenAI).

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Domain.Llm;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgenticSdlc.Modules.Llm;

/// <summary><see cref="ILlmClient"/> backed by a pool of <see cref="IChatClient"/> instances keyed by API key.</summary>
public sealed class PooledChatLlmClient : ILlmClient
{
    private readonly Func<string, string, IChatClient> _clientFactory;
    private readonly Func<IReadOnlyList<string>> _keyProvider;
    private readonly ApiKeyRouter _router;
    private readonly Func<Exception, bool> _isRateLimited;
    private readonly Func<Exception, TimeSpan?> _retryAfter;
    private readonly ILogger _logger;
    private readonly TimeSpan _baseDelay;
    private readonly ConcurrentDictionary<string, IChatClient> _clients = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public string Provider { get; }

    public PooledChatLlmClient(
        string provider,
        Func<string, string, IChatClient> clientFactory,
        Func<IReadOnlyList<string>> keyProvider,
        ApiKeyRouter router,
        Func<Exception, bool> isRateLimited,
        Func<Exception, TimeSpan?> retryAfter,
        ILogger logger,
        TimeSpan? baseDelay = null)
    {
        Provider = string.IsNullOrWhiteSpace(provider) ? throw new ArgumentException("provider required", nameof(provider)) : provider;
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _isRateLimited = isRateLimited ?? throw new ArgumentNullException(nameof(isRateLimited));
        _retryAfter = retryAfter ?? (_ => null);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _baseDelay = baseDelay ?? TimeSpan.FromSeconds(1);
    }

    /// <inheritdoc />
    public async Task<LlmResponse> SendAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        var keys = _keyProvider();
        if (keys.Count == 0)
        {
            throw new LlmException($"{Provider}: no API key configured.", Provider);
        }

        var messages = new List<ChatMessage>(2);
        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            messages.Add(new ChatMessage(ChatRole.System, request.SystemPrompt));
        }
        messages.Add(new ChatMessage(ChatRole.User, request.UserPrompt));
        var options = new ChatOptions
        {
            ModelId = request.Model,
            Temperature = (float)request.Temperature,
            MaxOutputTokens = request.MaxTokens,
        };

        var maxAttempts = Math.Max(1, keys.Count);
        Exception? last = null;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = _router.Acquire(Provider, keys)!;
            var chat = _clients.GetOrAdd($"{key} {request.Model}", _ => _clientFactory(key, request.Model));

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var response = await chat.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
                stopwatch.Stop();

                var inputTokens = (int)(response.Usage?.InputTokenCount ?? 0);
                var outputTokens = (int)(response.Usage?.OutputTokenCount ?? 0);
                return new LlmResponse(
                    Content: response.Text ?? string.Empty,
                    InputTokens: inputTokens,
                    OutputTokens: outputTokens,
                    CostUsd: CostCalculator.Calculate(request.Model, inputTokens, outputTokens),
                    Latency: stopwatch.Elapsed,
                    Model: request.Model,
                    Provider: Provider);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (_isRateLimited(ex))
            {
                last = ex;
                _router.Penalize(Provider, key, _retryAfter(ex));
                _logger.LogWarning("[{Provider}] key rate-limited; {Available}/{Total} keys available — failing over.",
                    Provider, _router.AvailableCount(Provider, keys), keys.Count);

                if (attempt + 1 < maxAttempts)
                {
                    var wait = TimeSpan.FromMilliseconds(_baseDelay.TotalMilliseconds * Math.Pow(2, attempt));
                    await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        throw new LlmException($"{Provider} rate-limited on all {keys.Count} key(s) after {maxAttempts} attempt(s).", Provider, innerException: last);
    }
}
