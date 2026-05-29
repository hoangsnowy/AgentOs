// AgenticSdlc.Infrastructure/Llm/AzureOpenAiClient.cs
// Sprint 1 — Azure OpenAI Chat Completions client (raw HttpClient, no SDK).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Application.Configuration;
using AgenticSdlc.Domain.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgenticSdlc.Infrastructure.Llm;

/// <summary>
/// Client calling Azure OpenAI Chat Completions at
/// <c>POST {endpoint}/openai/deployments/{model}/chat/completions?api-version={apiVersion}</c>.
/// Authentication: header <c>api-key</c>.
/// </summary>
public sealed class AzureOpenAiClient : ILlmClient
{
    /// <summary>Named HttpClient key used for <see cref="IHttpClientFactory"/>.</summary>
    public const string HttpClientName = "AgenticSdlc.AzureOpenAiClient";

    private readonly HttpClient _http;
    private readonly AzureOpenAiOptions _options;
    private readonly IRuntimeOverrides _overrides;
    private readonly ApiKeyRouter _router;
    private readonly ILogger<AzureOpenAiClient> _logger;

    /// <inheritdoc />
    public string Provider => "AzureOpenAI";

    /// <summary>Initializes the client. The api-key(s) + endpoint are read at request time from
    /// <paramref name="overrides"/> (runtime) + the configured pool; <paramref name="router"/> hands out
    /// keys round-robin with rate-limit failover. Keys in the pool share the one configured endpoint.</summary>
    public AzureOpenAiClient(HttpClient http, IOptions<LlmOptions> options, IRuntimeOverrides overrides, ApiKeyRouter router, ILogger<AzureOpenAiClient> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _options = options.Value?.AzureOpenAi ?? new AzureOpenAiOptions();
        _overrides = overrides ?? throw new ArgumentNullException(nameof(overrides));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        ConfigureHttpClient();
    }

    private void ConfigureHttpClient()
    {
        if (_http.BaseAddress is null && !string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            _http.BaseAddress = new Uri(_options.Endpoint.TrimEnd('/') + "/");
        }

        if (_http.Timeout == TimeSpan.FromSeconds(100) && _options.TimeoutSeconds > 0)
        {
            _http.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        }

        // api-key is attached per request so runtime overrides take effect.
    }

    private List<string> EffectiveApiKeys()
    {
        var keys = new List<string>();
        if (!string.IsNullOrWhiteSpace(_overrides.AzureApiKey)) { keys.Add(_overrides.AzureApiKey!); }
        foreach (var k in _options.ApiKeys)
        {
            if (!string.IsNullOrWhiteSpace(k)) { keys.Add(k); }
        }
        if (!string.IsNullOrWhiteSpace(_options.ApiKey)) { keys.Add(_options.ApiKey); }
        return keys.Distinct(StringComparer.Ordinal).ToList();
    }

    private string EffectiveEndpoint()
        => !string.IsNullOrWhiteSpace(_overrides.AzureEndpoint) ? _overrides.AzureEndpoint! : _options.Endpoint;

    /// <inheritdoc />
    public async Task<LlmResponse> SendAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        var keys = EffectiveApiKeys();
        var endpoint = EffectiveEndpoint();
        if (keys.Count == 0 || string.IsNullOrWhiteSpace(endpoint))
        {
            throw new LlmException(
                "Azure OpenAI not configured. Set api-key + endpoint on the Settings page, "
                + "or 'Llm:AzureOpenAi:ApiKey' / 'Llm:AzureOpenAi:ApiKeys' / 'Llm:AzureOpenAi:Endpoint' (user-secrets or env).",
                Provider);
        }

        var stopwatch = Stopwatch.StartNew();

        var messages = string.IsNullOrEmpty(request.SystemPrompt)
            ? new[]
            {
                new ChatMessageDto { Role = "user", Content = request.UserPrompt }
            }
            : new[]
            {
                new ChatMessageDto { Role = "system", Content = request.SystemPrompt },
                new ChatMessageDto { Role = "user", Content = request.UserPrompt }
            };

        var payload = new ChatRequestDto
        {
            Messages = messages,
            Temperature = request.Temperature,
            MaxTokens = request.MaxTokens,
        };

        var deployment = string.IsNullOrWhiteSpace(_options.Model) ? request.Model : _options.Model;
        // Build absolute URL when endpoint differs from BaseAddress so runtime endpoint overrides take effect.
        var relPath = $"openai/deployments/{deployment}/chat/completions?api-version={_options.ApiVersion}";
        var url = endpoint.TrimEnd('/') + "/" + relPath;

        var maxRetries = Math.Max(_options.MaxRetries, keys.Count - 1);
        var dto = await RetryPolicy.ExecuteAsync(
            async ct =>
            {
                var apiKey = _router.Acquire(Provider, keys)!;
                try
                {
                    return await PostOnceAsync(url, payload, apiKey, ct).ConfigureAwait(false);
                }
                catch (TransientHttpException ex) when (ex.StatusCode == 429)
                {
                    _router.Penalize(Provider, apiKey, ex.RetryAfter);
                    _logger.LogWarning(
                        "[AzureOpenAI] API key rate-limited (429); {Available}/{Total} keys still available — failing over.",
                        _router.AvailableCount(Provider, keys), keys.Count);
                    throw;
                }
            },
            maxRetries: maxRetries,
            baseDelay: TimeSpan.FromSeconds(1),
            logger: _logger,
            providerName: Provider,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        stopwatch.Stop();

        var content = ExtractText(dto);
        var inputTokens = dto.Usage?.PromptTokens ?? 0;
        var outputTokens = dto.Usage?.CompletionTokens ?? 0;
        var cost = CostCalculator.Calculate(request.Model, inputTokens, outputTokens);

        return new LlmResponse(
            Content: content,
            InputTokens: inputTokens,
            OutputTokens: outputTokens,
            CostUsd: cost,
            Latency: stopwatch.Elapsed,
            Model: dto.Model ?? request.Model,
            Provider: Provider);
    }

    private async Task<ChatResponseDto> PostOnceAsync(string url, ChatRequestDto payload, string apiKey, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload, options: JsonOpts),
        };
        req.Headers.Add("api-key", apiKey);

        using var response = await _http.SendAsync(req, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var statusCode = (int)response.StatusCode;
            var body = await SafeReadAsync(response, ct).ConfigureAwait(false);

            if (RetryPolicy.IsRetriableStatus(response.StatusCode))
            {
                throw new TransientHttpException(statusCode, $"AzureOpenAI returned {statusCode}: {body}", ParseRetryAfter(response));
            }

            throw new LlmException(
                $"AzureOpenAI returned non-retriable {statusCode}: {body}",
                Provider,
                statusCode);
        }

        ChatResponseDto? dto;
        try
        {
            dto = await response.Content.ReadFromJsonAsync<ChatResponseDto>(JsonOpts, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new LlmException("AzureOpenAI returned malformed JSON.", Provider, innerException: ex);
        }

        if (dto is null)
        {
            throw new LlmException("AzureOpenAI returned null body.", Provider);
        }

        return dto;
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            return "<unreadable>";
        }
    }

    /// <summary>Parse the <c>Retry-After</c> header (delta-seconds or HTTP-date) into a cooldown duration.</summary>
    private static TimeSpan? ParseRetryAfter(HttpResponseMessage resp)
    {
        var ra = resp.Headers.RetryAfter;
        if (ra is null)
        {
            return null;
        }
        if (ra.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }
        if (ra.Date is { } date)
        {
            var diff = date - DateTimeOffset.UtcNow;
            if (diff > TimeSpan.Zero)
            {
                return diff;
            }
        }
        return null;
    }

    private static string ExtractText(ChatResponseDto dto)
    {
        if (dto.Choices is null || dto.Choices.Length == 0)
        {
            return string.Empty;
        }

        return dto.Choices[0].Message?.Content ?? string.Empty;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    // ----- DTOs matching the Azure OpenAI chat shape -----

    private sealed class ChatRequestDto
    {
        [JsonPropertyName("messages")] public ChatMessageDto[] Messages { get; set; } = [];
        [JsonPropertyName("temperature")] public double Temperature { get; set; }
        [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; }
    }

    private sealed class ChatMessageDto
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "user";
        [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
    }

    private sealed class ChatResponseDto
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("choices")] public ChoiceDto[]? Choices { get; set; }
        [JsonPropertyName("usage")] public UsageDto? Usage { get; set; }
    }

    private sealed class ChoiceDto
    {
        [JsonPropertyName("index")] public int Index { get; set; }
        [JsonPropertyName("message")] public ChatMessageDto? Message { get; set; }
        [JsonPropertyName("finish_reason")] public string? FinishReason { get; set; }
    }

    private sealed class UsageDto
    {
        [JsonPropertyName("prompt_tokens")] public int PromptTokens { get; set; }
        [JsonPropertyName("completion_tokens")] public int CompletionTokens { get; set; }
        [JsonPropertyName("total_tokens")] public int TotalTokens { get; set; }
    }
}
