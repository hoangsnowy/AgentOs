// Mock LLM client that reads fixtures from disk. Used for offline tests and demos.

using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Domain.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgenticSdlc.Modules.Llm;

/// <summary>
/// Hashes (model + systemPrompt + userPrompt) → looks up a JSON fixture in the configured folder.
/// On hit → load fixture; on miss → stub response. Offline tests, CI without an API key, demos.
/// </summary>
public sealed class MockLlmClient : ILlmClient
{
    private readonly MockOptions _options;
    private readonly ILogger<MockLlmClient> _logger;

    /// <inheritdoc />
    public string Provider => "Mock";

    public MockLlmClient(IOptions<LlmOptions> options, ILogger<MockLlmClient> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value?.Mock ?? new MockOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<LlmResponse> SendAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        var stopwatch = Stopwatch.StartNew();

        if (_options.SimulatedLatencyMs > 0)
        {
            await Task.Delay(_options.SimulatedLatencyMs, cancellationToken).ConfigureAwait(false);
        }

        var hash = ComputeHash(request);
        var fixturePath = ResolveFixturePath(hash);

        MockFixture? fixture = null;
        if (fixturePath is not null && File.Exists(fixturePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(fixturePath, cancellationToken).ConfigureAwait(false);
                fixture = JsonSerializer.Deserialize<MockFixture>(json, JsonOpts);
                _logger.LogDebug("MockLlmClient hit fixture {Path} for hash {Hash}.", fixturePath, hash);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MockLlmClient failed to load fixture {Path}, falling back to stub.", fixturePath);
            }
        }
        else
        {
            _logger.LogDebug("MockLlmClient miss for hash {Hash} (path {Path}). Returning stub.", hash, fixturePath);
        }

        var content = fixture?.Content ?? "stub-response";
        var inputTokens = fixture?.InputTokens ?? EstimateTokens(request.SystemPrompt + request.UserPrompt);
        var outputTokens = fixture?.OutputTokens ?? EstimateTokens(content);
        var cost = CostCalculator.Calculate(request.Model, inputTokens, outputTokens);

        stopwatch.Stop();

        return new LlmResponse(
            Content: content,
            InputTokens: inputTokens,
            OutputTokens: outputTokens,
            CostUsd: cost,
            Latency: stopwatch.Elapsed,
            Model: request.Model,
            Provider: Provider);
    }

    /// <summary>SHA-256 hash of (model + systemPrompt + userPrompt). First 16 hex chars.</summary>
    public static string ComputeHash(LlmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var input = $"{request.Model}\n---\n{request.SystemPrompt}\n---\n{request.UserPrompt}";
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        var sb = new StringBuilder(16);
        for (var i = 0; i < 8; i++)
        {
            sb.Append(hash[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    private string? ResolveFixturePath(string hash)
    {
        if (string.IsNullOrWhiteSpace(_options.FixturePath))
        {
            return null;
        }

        var byHash = Path.Combine(_options.FixturePath, $"{hash}.json");
        if (File.Exists(byHash))
        {
            return byHash;
        }

        var fromBase = Path.Combine(AppContext.BaseDirectory, _options.FixturePath, $"{hash}.json");
        if (File.Exists(fromBase))
        {
            return fromBase;
        }

        return byHash;
    }

    private static int EstimateTokens(string text)
        => string.IsNullOrEmpty(text) ? 0 : Math.Max(1, text.Length / 4);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private sealed class MockFixture
    {
        public string Content { get; set; } = string.Empty;
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
    }
}
