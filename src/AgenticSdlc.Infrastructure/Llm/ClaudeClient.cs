// AgenticSdlc.Infrastructure/Llm/ClaudeClient.cs
// Sprint 1 — Anthropic Messages API client (raw HttpClient, no SDK).

using System;
using System.Diagnostics;
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
/// Client calling the Anthropic Messages API at <c>POST /v1/messages</c>.
/// Authentication: header <c>x-api-key</c>; version: header <c>anthropic-version</c>.
/// </summary>
public sealed class ClaudeClient : ILlmClient
{
    /// <summary>Named HttpClient key used for <see cref="IHttpClientFactory"/>.</summary>
    public const string HttpClientName = "AgenticSdlc.ClaudeClient";

    private readonly HttpClient _http;
    private readonly ClaudeOptions _options;
    private readonly IRuntimeOverrides _overrides;
    private readonly ILogger<ClaudeClient> _logger;

    /// <inheritdoc />
    public string Provider => "Claude";

    /// <summary>Initializes the client. <paramref name="http"/> should be resolved via <see cref="IHttpClientFactory"/>.
    /// The API key is read at request time from <paramref name="overrides"/> (runtime, Settings UI) with the
    /// <c>Llm:Claude:ApiKey</c> appsettings value as fallback — so swapping a key in the UI takes effect immediately.</summary>
    public ClaudeClient(HttpClient http, IOptions<LlmOptions> options, IRuntimeOverrides overrides, ILogger<ClaudeClient> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _options = options.Value?.Claude ?? new ClaudeOptions();
        _overrides = overrides ?? throw new ArgumentNullException(nameof(overrides));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        ConfigureHttpClient();
    }

    private void ConfigureHttpClient()
    {
        if (_http.BaseAddress is null && !string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            _http.BaseAddress = new Uri(_options.Endpoint.TrimEnd('/') + "/");
        }

        // Timeout: prefer the value already set externally (via AddHttpClient), fall back to options.
        if (_http.Timeout == TimeSpan.FromSeconds(100) /* default */ && _options.TimeoutSeconds > 0)
        {
            _http.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        }

        // Static (provider-level) header only — the api-key is attached per request so runtime overrides take effect.
        if (!_http.DefaultRequestHeaders.Contains("anthropic-version"))
        {
            _http.DefaultRequestHeaders.Add("anthropic-version", _options.ApiVersion);
        }
    }

    /// <summary>Resolve the effective API key: runtime override (Settings UI) wins; falls back to appsettings.</summary>
    private string EffectiveApiKey()
        => !string.IsNullOrWhiteSpace(_overrides.AnthropicApiKey) ? _overrides.AnthropicApiKey! : _options.ApiKey;

    /// <inheritdoc />
    public async Task<LlmResponse> SendAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        var apiKey = EffectiveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new LlmException(
                "Anthropic API key not configured. Set it on the Settings page, or 'Llm:Claude:ApiKey' (user-secrets or env), "
                + "or set 'Llm:ForceProvider=AzureOpenAI' to run the whole pipeline on Azure only.",
                Provider);
        }

        var stopwatch = Stopwatch.StartNew();

        var payload = new ClaudeRequestDto
        {
            Model = request.Model,
            MaxTokens = request.MaxTokens,
            Temperature = request.Temperature,
            System = string.IsNullOrEmpty(request.SystemPrompt) ? null : request.SystemPrompt,
            Messages = new[]
            {
                new ClaudeMessageDto { Role = "user", Content = request.UserPrompt }
            },
        };

        var dto = await RetryPolicy.ExecuteAsync(
            async ct => await PostOnceAsync(payload, apiKey, ct).ConfigureAwait(false),
            maxRetries: _options.MaxRetries,
            baseDelay: TimeSpan.FromSeconds(1),
            logger: _logger,
            providerName: Provider,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        stopwatch.Stop();

        var content = ExtractText(dto);
        var inputTokens = dto.Usage?.InputTokens ?? 0;
        var outputTokens = dto.Usage?.OutputTokens ?? 0;
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

    /// <summary>A single POST. Sets the <c>x-api-key</c> header per request so runtime overrides take effect.
    /// Throws <see cref="TransientHttpException"/> on 429/5xx, <see cref="LlmException"/> on other 4xx or malformed responses.</summary>
    private async Task<ClaudeResponseDto> PostOnceAsync(ClaudeRequestDto payload, string apiKey, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
        {
            Content = JsonContent.Create(payload, options: JsonOpts),
        };
        req.Headers.Add("x-api-key", apiKey);

        using var response = await _http.SendAsync(req, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var statusCode = (int)response.StatusCode;
            var body = await SafeReadAsync(response, ct).ConfigureAwait(false);

            if (RetryPolicy.IsRetriableStatus(response.StatusCode))
            {
                throw new TransientHttpException(statusCode, $"Claude API returned {statusCode}: {body}");
            }

            throw new LlmException(
                $"Claude API returned non-retriable {statusCode}: {body}",
                Provider,
                statusCode);
        }

        ClaudeResponseDto? dto;
        try
        {
            dto = await response.Content.ReadFromJsonAsync<ClaudeResponseDto>(JsonOpts, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new LlmException("Claude API returned malformed JSON.", Provider, innerException: ex);
        }

        if (dto is null)
        {
            throw new LlmException("Claude API returned null body.", Provider);
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

    private static string ExtractText(ClaudeResponseDto dto)
    {
        if (dto.Content is null || dto.Content.Length == 0)
        {
            return string.Empty;
        }

        // Anthropic returns an array of content blocks; only take type="text".
        var parts = new System.Text.StringBuilder();
        foreach (var block in dto.Content)
        {
            if (block.Type == "text" && !string.IsNullOrEmpty(block.Text))
            {
                parts.Append(block.Text);
            }
        }
        return parts.ToString();
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    // ----- DTOs (internal, shape matching the Anthropic API) -----

    private sealed class ClaudeRequestDto
    {
        [JsonPropertyName("model")] public string Model { get; set; } = string.Empty;
        [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; }
        [JsonPropertyName("temperature")] public double Temperature { get; set; }
        [JsonPropertyName("system")] public string? System { get; set; }
        [JsonPropertyName("messages")] public ClaudeMessageDto[] Messages { get; set; } = [];
    }

    private sealed class ClaudeMessageDto
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "user";
        [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
    }

    private sealed class ClaudeResponseDto
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("content")] public ClaudeContentBlockDto[]? Content { get; set; }
        [JsonPropertyName("usage")] public ClaudeUsageDto? Usage { get; set; }
    }

    private sealed class ClaudeContentBlockDto
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "text";
        [JsonPropertyName("text")] public string? Text { get; set; }
    }

    private sealed class ClaudeUsageDto
    {
        [JsonPropertyName("input_tokens")] public int InputTokens { get; set; }
        [JsonPropertyName("output_tokens")] public int OutputTokens { get; set; }
    }
}
