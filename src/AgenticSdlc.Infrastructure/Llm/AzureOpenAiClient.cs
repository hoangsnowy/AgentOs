// AgenticSdlc.Infrastructure/Llm/AzureOpenAiClient.cs
// Sprint 1 — Azure OpenAI Chat Completions client (raw HttpClient, no SDK).

using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly ILogger<AzureOpenAiClient> _logger;

    /// <inheritdoc />
    public string Provider => "AzureOpenAI";

    /// <summary>Initializes the client.</summary>
    public AzureOpenAiClient(HttpClient http, IOptions<LlmOptions> options, ILogger<AzureOpenAiClient> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _options = options.Value?.AzureOpenAi ?? new AzureOpenAiOptions();
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

        if (!_http.DefaultRequestHeaders.Contains("api-key") && !string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _http.DefaultRequestHeaders.Add("api-key", _options.ApiKey);
        }
    }

    /// <inheritdoc />
    public async Task<LlmResponse> SendAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

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
        var url = $"openai/deployments/{deployment}/chat/completions?api-version={_options.ApiVersion}";

        var dto = await RetryPolicy.ExecuteAsync(
            async ct => await PostOnceAsync(url, payload, ct).ConfigureAwait(false),
            maxRetries: _options.MaxRetries,
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

    private async Task<ChatResponseDto> PostOnceAsync(string url, ChatRequestDto payload, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync(url, payload, JsonOpts, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var statusCode = (int)response.StatusCode;
            var body = await SafeReadAsync(response, ct).ConfigureAwait(false);

            if (RetryPolicy.IsRetriableStatus(response.StatusCode))
            {
                throw new TransientHttpException(statusCode, $"AzureOpenAI returned {statusCode}: {body}");
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
