// AgenticSdlc.Infrastructure/Llm/ChatClientLlmClient.cs
// Provider-neutral bridge from Microsoft.Extensions.AI IChatClient to the app's ILlmClient port.
// This is the substrate for the SDK-based gateway: each provider (Azure OpenAI via Azure.AI.OpenAI,
// Claude via Anthropic.SDK) produces an IChatClient, wrapped here into the cost/latency-shaped
// LlmResponse the agents expect. Replaces the hand-rolled raw-HttpClient clients.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Domain.Llm;
using Microsoft.Extensions.AI;

namespace AgenticSdlc.Infrastructure.Llm;

/// <summary>
/// Adapts a <see cref="IChatClient"/> (the official-SDK abstraction) to <see cref="ILlmClient"/>.
/// Maps the provider-neutral <see cref="LlmRequest"/> to a chat call and the reply (text + token usage)
/// back to an <see cref="LlmResponse"/>, attaching the estimated cost.
/// </summary>
public sealed class ChatClientLlmClient : ILlmClient
{
    private readonly IChatClient _chat;

    /// <inheritdoc />
    public string Provider { get; }

    /// <summary>Wraps <paramref name="chat"/>; <paramref name="provider"/> tags the response.</summary>
    public ChatClientLlmClient(IChatClient chat, string provider)
    {
        _chat = chat ?? throw new ArgumentNullException(nameof(chat));
        Provider = string.IsNullOrWhiteSpace(provider) ? "ChatClient" : provider;
    }

    /// <inheritdoc />
    public async Task<LlmResponse> SendAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

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

        var stopwatch = Stopwatch.StartNew();
        ChatResponse response;
        try
        {
            response = await _chat.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new LlmException($"{Provider} chat request failed: {ex.Message}", Provider, innerException: ex);
        }
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
}
