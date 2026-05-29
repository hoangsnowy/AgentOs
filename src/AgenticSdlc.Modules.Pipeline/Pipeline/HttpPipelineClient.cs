// AgenticSdlc.Infrastructure/Pipeline/HttpPipelineClient.cs
// Phase 8 — Remote IPipelineClient. POSTs to /pipeline/stream and parses the SSE response.
// The wire shape:
//   event: progress
//   data: {<PipelineProgressEvent JSON>}
//   ...
//   event: result        OR    event: error
//   data: {<PipelineResult JSON>}    data: "{message}"

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.SharedKernel.Identity;
using AgenticSdlc.Modules.Pipeline.Pipeline;
using AgenticSdlc.Domain.Pipeline;
using AgenticSdlc.Domain.Requirements;
using Microsoft.Extensions.Logging;

namespace AgenticSdlc.Modules.Pipeline.Pipeline;

/// <summary>
/// HTTP-backed <see cref="IPipelineClient"/> — POSTs to <c>{ApiBaseUrl}/pipeline/stream</c> and
/// translates the Server-Sent Events response stream back into <see cref="PipelineStreamEvent"/>s.
/// </summary>
public sealed class HttpPipelineClient : IPipelineClient
{
    /// <summary>Named HttpClient registered by <c>AddPipelineClient</c>.</summary>
    public const string HttpClientName = "PipelineApi";

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpFactory;
    private readonly IAuthTokenProvider _auth;
    private readonly ILogger<HttpPipelineClient> _logger;

    /// <summary>Construct with the named <see cref="IHttpClientFactory"/> + auth + logger.</summary>
    public HttpPipelineClient(IHttpClientFactory httpFactory, IAuthTokenProvider auth, ILogger<HttpPipelineClient> logger)
    {
        _httpFactory = httpFactory;
        _auth = auth;
        _logger = logger;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<PipelineStreamEvent> StreamAsync(
        UserStory story,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(story);

        using var client = _httpFactory.CreateClient(HttpClientName);
        using var req = new HttpRequestMessage(HttpMethod.Post, "pipeline/stream")
        {
            Content = JsonContent.Create(story, options: JsonOpts),
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var token = await _auth.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(token))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var msg = $"Pipeline API returned {(int)resp.StatusCode} {resp.ReasonPhrase}";
            _logger.LogError("{Msg}", msg);
            yield return new PipelineStreamEvent(PipelineStreamEventKind.Error, Error: msg);
            yield break;
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new System.IO.StreamReader(stream);

        string? currentEvent = null;
        string? currentData = null;

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) { break; }

            if (line.Length == 0)
            {
                // Dispatch the accumulated event.
                if (currentEvent is not null && currentData is not null)
                {
                    var evt = ParseLine(currentEvent, currentData);
                    if (evt is not null)
                    {
                        yield return evt;
                        if (evt.Kind != PipelineStreamEventKind.Progress) { yield break; }
                    }
                }
                currentEvent = null;
                currentData = null;
                continue;
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                currentEvent = line.Substring(6).Trim();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                currentData = line.Substring(5).Trim();
            }
        }
    }

    private static PipelineStreamEvent? ParseLine(string kind, string data) => kind switch
    {
        "progress" => new PipelineStreamEvent(
            PipelineStreamEventKind.Progress,
            Progress: JsonSerializer.Deserialize<PipelineProgressEvent>(data, JsonOpts)),
        "result" => new PipelineStreamEvent(
            PipelineStreamEventKind.Result,
            Result: JsonSerializer.Deserialize<PipelineResult>(data, JsonOpts)),
        "error" => new PipelineStreamEvent(
            PipelineStreamEventKind.Error,
            Error: JsonSerializer.Deserialize<string>(data, JsonOpts)),
        _ => null,
    };
}
