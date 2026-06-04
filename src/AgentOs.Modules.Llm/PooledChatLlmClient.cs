// SDK-based provider client: a keyed pool of Microsoft.Extensions.AI IChatClient instances (one per
// API key) selected by ApiKeyRouter, with round-robin + rate-limit (429) failover. Provider-agnostic
// — the key->IChatClient factory and the "is this a rate-limit error" predicate are injected, so the
// same class serves Claude (Anthropic.SDK) and Azure OpenAI (Azure.AI.OpenAI).

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Llm;
using AgentOs.Domain.Tools;
using AgentOs.SharedKernel.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentOs.Modules.Llm;

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
    private readonly IToolRegistry? _toolRegistry;
    private readonly ITenantContext? _tenantContext;
    private readonly IToolPolicy? _toolPolicy;
    private readonly IToolInvocationLog? _toolInvocationLog;
    private readonly ConcurrentDictionary<string, IChatClient> _clients = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IChatClient> _wrappedClients = new(StringComparer.Ordinal);

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
        TimeSpan? baseDelay = null,
        IToolRegistry? toolRegistry = null,
        ITenantContext? tenantContext = null,
        IToolPolicy? toolPolicy = null,
        IToolInvocationLog? toolInvocationLog = null)
    {
        Provider = string.IsNullOrWhiteSpace(provider) ? throw new ArgumentException("provider required", nameof(provider)) : provider;
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _isRateLimited = isRateLimited ?? throw new ArgumentNullException(nameof(isRateLimited));
        _retryAfter = retryAfter ?? (_ => null);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _baseDelay = baseDelay ?? TimeSpan.FromSeconds(1);
        _toolRegistry = toolRegistry;
        _tenantContext = tenantContext;
        _toolPolicy = toolPolicy;
        _toolInvocationLog = toolInvocationLog;
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
        var resolvedTools = ResolveTools(request.Tools);
        var options = new ChatOptions
        {
            ModelId = request.Model,
            Temperature = (float)request.Temperature,
            MaxOutputTokens = request.MaxTokens,
            Tools = resolvedTools.Count > 0 ? resolvedTools.Cast<AITool>().ToList() : null,
        };

        var maxAttempts = Math.Max(1, keys.Count);
        Exception? last = null;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = _router.Acquire(Provider, keys)!;
            var clientCacheKey = $"{key} {request.Model}";
            var chat = _clients.GetOrAdd(clientCacheKey, _ => _clientFactory(key, request.Model));
            if (resolvedTools.Count > 0)
            {
                // FunctionInvokingChatClient wrapper drives the tool-call loop transparently so the
                // ILlmClient.SendAsync contract still returns one LlmResponse — the final text turn.
                chat = _wrappedClients.GetOrAdd(clientCacheKey, _ => chat.AsBuilder().UseFunctionInvocation().Build());
            }

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

    private List<AIToolFunction> ResolveTools(IReadOnlyList<string>? requested)
    {
        var resolved = new List<AIToolFunction>();
        if (requested is null || requested.Count == 0 || _toolRegistry is null)
        {
            return resolved;
        }

        var tenantId = AgentOs.SharedKernel.Identity.AmbientIdentity.Current?.TenantId ?? _tenantContext?.TenantId ?? "anonymous";
        foreach (var name in requested)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }
            var tool = _toolRegistry.Resolve(name);
            if (tool is null)
            {
                _logger.LogWarning("[{Provider}] tool '{Tool}' requested but not registered — dropped.", Provider, name);
                continue;
            }
            resolved.Add(new AIToolFunction(tool, tenantId, runId: null, policy: _toolPolicy, log: _toolInvocationLog));
        }
        return resolved;
    }
}
