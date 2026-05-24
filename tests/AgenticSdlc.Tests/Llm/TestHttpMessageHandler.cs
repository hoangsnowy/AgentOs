// AgenticSdlc.Tests/Llm/TestHttpMessageHandler.cs
// Sprint 1 — Queue-based HttpMessageHandler helper used to test ClaudeClient/AzureOpenAiClient.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AgenticSdlc.Tests.Llm;

/// <summary>
/// A handler that returns responses in queue order. Each SendAsync call dequeues one entry.
/// Supports an exception (throw) or an HttpResponseMessage (return).
/// </summary>
internal sealed class TestHttpMessageHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<Func<HttpRequestMessage, HttpResponseMessage>> _responders = new();
    public int CallCount { get; private set; }
    public List<HttpRequestMessage> CapturedRequests { get; } = new();

    public TestHttpMessageHandler EnqueueResponse(HttpStatusCode status, string content = "{}", string mediaType = "application/json")
    {
        _responders.Enqueue(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(content, System.Text.Encoding.UTF8, mediaType),
        });
        return this;
    }

    public TestHttpMessageHandler EnqueueThrow(Exception ex)
    {
        _responders.Enqueue(_ => throw ex);
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        CapturedRequests.Add(request);
        if (!_responders.TryDequeue(out var responder))
        {
            throw new InvalidOperationException("TestHttpMessageHandler: no more responders queued.");
        }
        var resp = responder(request);
        return Task.FromResult(resp);
    }
}
