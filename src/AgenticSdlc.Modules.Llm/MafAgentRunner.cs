// Atomic Microsoft Agent Framework call: create an AIAgent over an Azure OpenAI IChatClient and
// run one turn. The building block the MAF orchestrator composes.

using System;
using System.ClientModel;
using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Domain.Llm;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgenticSdlc.Modules.Llm;

/// <summary>Creates and runs Microsoft Agent Framework <see cref="AIAgent"/>s over Azure OpenAI.</summary>
public sealed class MafAgentRunner
{
    private readonly IChatClient _chat;

    public MafAgentRunner(AzureOpenAiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ApiKey) || string.IsNullOrWhiteSpace(options.Endpoint))
        {
            throw new LlmException("Azure OpenAI not configured (Llm:AzureOpenAi:ApiKey + Endpoint).", "MAF");
        }
        var azure = new AzureOpenAIClient(new Uri(options.Endpoint), new ApiKeyCredential(options.ApiKey));
        var deployment = string.IsNullOrWhiteSpace(options.Model) ? "gpt-4.1" : options.Model;
        _chat = azure.GetChatClient(deployment).AsIChatClient();
    }

    /// <summary>Create a named MAF agent with a system instruction.</summary>
    public AIAgent CreateAgent(string name, string instructions)
        => new ChatClientAgent(_chat, instructions: instructions, name: name);

    /// <summary>Run one turn against the agent; returns text + token usage.</summary>
    public static async Task<MafRunResult> RunAsync(AIAgent agent, string userPrompt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        AgentResponse response = await agent.RunAsync(userPrompt, cancellationToken: ct).ConfigureAwait(false);
        var usage = response.Usage;
        return new MafRunResult(
            response.Text ?? string.Empty,
            (int)(usage?.InputTokenCount ?? 0),
            (int)(usage?.OutputTokenCount ?? 0));
    }
}

/// <summary>Result of one MAF agent turn.</summary>
public sealed record MafRunResult(string Text, int InputTokens, int OutputTokens);
