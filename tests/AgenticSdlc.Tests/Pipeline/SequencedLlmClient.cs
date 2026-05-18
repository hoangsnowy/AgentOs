// AgenticSdlc.Tests/Pipeline/SequencedLlmClient.cs
// Phase 5 — Test ILlmClient trả response theo thứ tự gọi (1 canned response / call).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Domain.Llm;

namespace AgenticSdlc.Tests.Pipeline;

/// <summary>
/// ILlmClient cho integration test: nhận 1 queue (model, content) khi khởi tạo, trả
/// response theo thứ tự gọi. Khác MockLlmClient (hash-based) — đơn giản hoá test E2E
/// nơi hash-driven fixture brittle.
/// </summary>
public sealed class SequencedLlmClient : ILlmClient
{
    private readonly Queue<CannedResponse> _queue;
    private int _callIndex;

    public string Provider => "Sequenced";

    public IReadOnlyList<LlmRequest> CapturedRequests => _captured;
    private readonly List<LlmRequest> _captured = new();

    public SequencedLlmClient(IEnumerable<CannedResponse> responses)
    {
        ArgumentNullException.ThrowIfNull(responses);
        _queue = new Queue<CannedResponse>(responses);
    }

    public Task<LlmResponse> SendAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _captured.Add(request);

        if (_queue.Count == 0)
        {
            throw new InvalidOperationException($"SequencedLlmClient exhausted after {_callIndex} calls.");
        }

        var canned = _queue.Dequeue();
        _callIndex++;

        return Task.FromResult(new LlmResponse(
            Content: canned.Content,
            InputTokens: canned.InputTokens,
            OutputTokens: canned.OutputTokens,
            CostUsd: canned.CostUsd,
            Latency: canned.Latency,
            Model: canned.Model ?? request.Model,
            Provider: Provider));
    }
}

/// <summary>1 canned response.</summary>
public sealed record CannedResponse(
    string Content,
    int InputTokens = 100,
    int OutputTokens = 200,
    decimal CostUsd = 0.0001m,
    TimeSpan Latency = default,
    string? Model = null);
