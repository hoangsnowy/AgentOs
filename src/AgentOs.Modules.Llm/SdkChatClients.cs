// Factories that produce a Microsoft.Extensions.AI IChatClient from the official SDK clients —
// Azure OpenAI via Azure.AI.OpenAI, Claude via Anthropic.SDK — plus a cross-SDK rate-limit predicate.
// Consumed by PooledChatLlmClient (one IChatClient per API key).

using System;
using System.ClientModel;
using System.Net;
using System.Net.Http;
using Anthropic.SDK;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;

namespace AgentOs.Modules.Llm;

internal static class SdkChatClients
{
    /// <summary>Claude via Anthropic.SDK — its Messages endpoint implements <see cref="IChatClient"/>.
    /// Wrapped so the owning <see cref="AnthropicClient"/> is disposed with the chat client (the pool
    /// owns the result for the process lifetime and disposes it on shutdown).</summary>
    public static IChatClient CreateClaude(string apiKey)
    {
        var client = new AnthropicClient(new APIAuthentication(apiKey));
        return new OwningChatClient(client.Messages, client);
    }

    /// <summary>Azure OpenAI via the official SDK, surfaced as <see cref="IChatClient"/>.</summary>
    public static IChatClient CreateAzure(string apiKey, string endpoint, string deployment)
        => new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey))
            .GetChatClient(deployment)
            .AsIChatClient();

    /// <summary>True when an exception represents an HTTP 429 / rate-limit / overloaded across the
    /// Azure (ClientResultException) and Anthropic (HttpRequestException) SDK shapes.</summary>
    public static bool IsRateLimited(Exception? ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                case ClientResultException cre when cre.Status == 429:
                    return true;
                case HttpRequestException hre when hre.StatusCode == HttpStatusCode.TooManyRequests:
                    return true;
            }

            var msg = e.Message;
            if (!string.IsNullOrEmpty(msg) &&
                (msg.Contains("429", StringComparison.Ordinal)
                 || msg.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
                 || msg.Contains("too many requests", StringComparison.OrdinalIgnoreCase)
                 || msg.Contains("overloaded", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }
        return false;
    }
}
