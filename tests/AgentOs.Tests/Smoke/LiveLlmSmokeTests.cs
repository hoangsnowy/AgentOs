// AgentOs.Tests/Smoke/LiveLlmSmokeTests.cs
// Smoke test that calls the real LLM through the SDK-based gateway (Anthropic.SDK / Azure.AI.OpenAI).
// Skipped by default; runs when RUN_LIVE_LLM=1 and an API key is present.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Llm;
using AgentOs.Modules.Llm;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Smoke;

public class LiveLlmSmokeTests
{
    private const string RunFlag = "RUN_LIVE_LLM";
    private const string AnthropicKey = "ANTHROPIC_API_KEY";
    private const string AzureKey = "AZURE_OPENAI_API_KEY";
    private const string AzureEndpoint = "AZURE_OPENAI_ENDPOINT";

    [Fact]
    public async Task Claude_LiveCall_NonEmptyResponse()
    {
        SkipUnlessEnabled(requireProvider: AnthropicKey);

        var key = Environment.GetEnvironmentVariable(AnthropicKey)!;
        var model = Environment.GetEnvironmentVariable("ANTHROPIC_MODEL") ?? "claude-haiku-4-5";
        var client = new PooledChatLlmClient(
            "Claude",
            (k, _) => SdkChatClients.CreateClaude(k),
            _ => ValueTask.FromResult<IReadOnlyList<string>>(new List<string> { key }),
            new ApiKeyRouter(TimeProvider.System),
            SdkChatClients.IsRateLimited, _ => null, NullLogger<PooledChatLlmClient>.Instance);

        var request = new LlmRequest(
            SystemPrompt: "You are a concise assistant. Reply in one sentence.",
            UserPrompt: "Say hello in Vietnamese.",
            Model: model,
            Temperature: 0.0,
            MaxTokens: 100);

        var response = await client.SendAsync(request, CancellationToken.None);

        response.Content.ShouldNotBeNullOrWhiteSpace();
        response.OutputTokens.ShouldBeGreaterThan(0);
        response.Latency.ShouldBeLessThan(TimeSpan.FromSeconds(30));
        response.Provider.ShouldBe("Claude");
    }

    [Fact]
    public async Task AzureOpenAi_LiveCall_NonEmptyResponse()
    {
        SkipUnlessEnabled(requireProvider: AzureKey);
        var endpoint = Environment.GetEnvironmentVariable(AzureEndpoint);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            Assert.Skip($"{AzureEndpoint} not set.");
        }

        var key = Environment.GetEnvironmentVariable(AzureKey)!;
        var deployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT") ?? "gpt-4.1";
        var client = new PooledChatLlmClient(
            "AzureOpenAI",
            (k, m) => SdkChatClients.CreateAzure(k, endpoint!, string.IsNullOrWhiteSpace(m) ? deployment : m),
            _ => ValueTask.FromResult<IReadOnlyList<string>>(new List<string> { key }),
            new ApiKeyRouter(TimeProvider.System),
            SdkChatClients.IsRateLimited, _ => null, NullLogger<PooledChatLlmClient>.Instance);

        var request = new LlmRequest(
            SystemPrompt: "You are a concise assistant. Reply in one sentence.",
            UserPrompt: "Say hello in Vietnamese.",
            Model: deployment,
            Temperature: 0.0,
            MaxTokens: 100);

        var response = await client.SendAsync(request, CancellationToken.None);

        response.Content.ShouldNotBeNullOrWhiteSpace();
        response.OutputTokens.ShouldBeGreaterThan(0);
        response.Latency.ShouldBeLessThan(TimeSpan.FromSeconds(30));
        response.Provider.ShouldBe("AzureOpenAI");
    }

    private static void SkipUnlessEnabled(string requireProvider)
    {
        if (Environment.GetEnvironmentVariable(RunFlag) != "1")
        {
            Assert.Skip($"{RunFlag} != 1 — set to 1 to run the live smoke test (cost ~$0.01/run).");
        }
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(requireProvider)))
        {
            Assert.Skip($"{requireProvider} not set.");
        }
    }
}
