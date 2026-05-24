// AgenticSdlc.Tests/Smoke/LiveLlmSmokeTests.cs
// Sprint 5 — smoke test that calls the real LLM. Skipped by default, runs when RUN_LIVE_LLM=1 + an API key is present.
// Doc: docs/RUN_LIVE_SMOKE.md.

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Domain.Llm;
using AgenticSdlc.Infrastructure.Llm;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AgenticSdlc.Tests.Smoke;

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

        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var opts = Options.Create(new LlmOptions
        {
            Provider = "Claude",
            Claude = new ClaudeOptions
            {
                ApiKey = Environment.GetEnvironmentVariable(AnthropicKey)!,
                Model = Environment.GetEnvironmentVariable("ANTHROPIC_MODEL") ?? "claude-haiku-4-5",
                TimeoutSeconds = 30,
                MaxRetries = 1,
            },
        });
        var client = new ClaudeClient(http, opts, NullLogger<ClaudeClient>.Instance);

        var request = new LlmRequest(
            SystemPrompt: "You are a concise assistant. Reply in one sentence.",
            UserPrompt: "Say hello in Vietnamese.",
            Model: opts.Value.Claude.Model,
            Temperature: 0.0,
            MaxTokens: 100);

        var response = await client.SendAsync(request, CancellationToken.None);

        response.Content.ShouldNotBeNullOrWhiteSpace();
        response.InputTokens.ShouldBeGreaterThan(0);
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

        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var opts = Options.Create(new LlmOptions
        {
            Provider = "AzureOpenAI",
            AzureOpenAi = new AzureOpenAiOptions
            {
                ApiKey = Environment.GetEnvironmentVariable(AzureKey)!,
                Endpoint = endpoint!,
                Model = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT") ?? "gpt-4.1",
                TimeoutSeconds = 30,
                MaxRetries = 1,
            },
        });
        var client = new AzureOpenAiClient(http, opts, NullLogger<AzureOpenAiClient>.Instance);

        var request = new LlmRequest(
            SystemPrompt: "You are a concise assistant. Reply in one sentence.",
            UserPrompt: "Say hello in Vietnamese.",
            Model: opts.Value.AzureOpenAi.Model,
            Temperature: 0.0,
            MaxTokens: 100);

        var response = await client.SendAsync(request, CancellationToken.None);

        response.Content.ShouldNotBeNullOrWhiteSpace();
        response.InputTokens.ShouldBeGreaterThan(0);
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
