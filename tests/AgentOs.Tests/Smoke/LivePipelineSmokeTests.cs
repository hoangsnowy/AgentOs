using AgentOs.SharedKernel.Modularity;
// AgentOs.Tests/Smoke/LivePipelineSmokeTests.cs
// Sprint 6 — live end-to-end pipeline (Requirement → Coding → Testing → QA) calling the real LLM.
// Skipped by default. Budget guard: max 5 LLM calls, max $0.50.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using AgentOs.Modules.Pipeline.Agents;
using AgentOs.Modules.Pipeline.Metrics;
using AgentOs.Domain.Pipeline;
using AgentOs.Modules.Llm;
using AgentOs.Modules.Pipeline.Validation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Smoke;

public class LivePipelineSmokeTests
{
    private const string RunFlag = "RUN_LIVE_LLM";
    private const string AnthropicKey = "ANTHROPIC_API_KEY";
    private const string AzureKey = "AZURE_OPENAI_API_KEY";
    private const string AzureEndpoint = "AZURE_OPENAI_ENDPOINT";

    private const decimal MaxBudgetUsd = 0.50m;
    private const int MaxCallCount = 5;

    [Fact]
    public async Task LivePipeline_ClaudeOnly_RunsWithinBudget()
        => await RunPipelineAsync(provider: "Claude", outputFile: "live_pipeline_smoke_claude.json");

    [Fact]
    public async Task LivePipeline_AzureOpenAi_RunsWithinBudget()
        => await RunPipelineAsync(provider: "AzureOpenAI", outputFile: "live_pipeline_smoke_azure.json");

    private static async Task RunPipelineAsync(string provider, string outputFile)
    {
        SkipIfNotEnabled(provider);

        var cfg = BuildConfig(provider);
        var sc = new ServiceCollection();
        sc.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        sc.AddSingleton<IConfiguration>(cfg);
        sc.AddModulesFromAssemblies(cfg, typeof(AgentOs.Modules.Llm.LlmModule).Assembly, typeof(AgentOs.Modules.AppConfig.AppConfigModule).Assembly);
        sc.AddValidation();
        sc.AddInMemoryMetrics();
        sc.AddAgents(cfg);
        await using var sp = sc.BuildServiceProvider();

        var orch = sp.GetRequiredService<IOrchestratorAgent>();
        var collector = sp.GetRequiredService<InMemoryMetricsCollector>();

        var userStory = new UserStory(
            "A simple TODO list system: users create a task with a title, mark it complete, and delete a task. Supports listing incomplete tasks.",
            NMax: 1);

        PipelineResult? result = null;
        Exception? failure = null;
        using (MetricsContext.BeginScope($"live-{Guid.NewGuid():N}", "LIVE-PIPELINE", 1))
        {
            try
            {
                result = await orch.RunAsync(userStory);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        }

        var snapshot = collector.Snapshot();
        var totalCost = snapshot.Sum(m => m.CostUsd);
        var callCount = snapshot.Count;

        // Capture the transcript before assertions so we still get a file even if an assertion fails.
        var transcriptPath = Path.Join(AppContext.BaseDirectory, "TestResults", outputFile);
        Directory.CreateDirectory(Path.GetDirectoryName(transcriptPath)!);
        var transcript = new
        {
            timestamp = DateTimeOffset.UtcNow,
            provider,
            userStory = userStory.Description,
            nMax = userStory.NMax,
            resultStatus = result?.Status.ToString() ?? "Exception",
            iterationCount = result?.IterationCount ?? 0,
            failure = failure?.Message,
            failureType = failure?.GetType().Name,
            totalCostUsd = totalCost,
            callCount,
            metrics = snapshot,
            specTitle = result?.Spec?.Title,
            codeFileCount = result?.Code?.Files.Count ?? 0,
            testFileCount = result?.Tests?.Files.Count ?? 0,
            qaScore = (result?.QaHistory?.Count ?? 0) > 0 ? result!.QaHistory[^1].Score : (double?)null,
        };
        File.WriteAllText(transcriptPath, JsonSerializer.Serialize(transcript, JsonOpts));

        if (failure is not null)
        {
            throw new Xunit.Sdk.XunitException($"Live pipeline threw {failure.GetType().Name}: {failure.Message}");
        }

        totalCost.ShouldBeLessThanOrEqualTo(MaxBudgetUsd, $"Budget exceeded: ${totalCost:F4}");
        callCount.ShouldBeLessThanOrEqualTo(MaxCallCount, $"Too many calls: {callCount}");
        result!.Status.ShouldBeOneOf(PipelineStatus.Done, PipelineStatus.MaxIterationReached);
    }

    private static IConfiguration BuildConfig(string provider)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Llm:Provider"] = provider,
            ["Pipeline:MaxIterations"] = "1",
        };
        if (string.Equals(provider, "Claude", StringComparison.OrdinalIgnoreCase))
        {
            var model = Environment.GetEnvironmentVariable("ANTHROPIC_MODEL") ?? "claude-haiku-4-5";
            settings["Llm:Claude:ApiKey"] = Environment.GetEnvironmentVariable(AnthropicKey)!;
            settings["Llm:Claude:Model"] = model;
            foreach (var agent in new[] { "Requirement", "Coding", "Testing", "Qa" })
            {
                settings[$"Agents:{agent}:Provider"] = "Claude";
                settings[$"Agents:{agent}:Model"] = model;
                settings[$"Agents:{agent}:Temperature"] = "0.1";
                settings[$"Agents:{agent}:MaxTokens"] = agent == "Coding" ? "3000" : "1500";
            }
        }
        else
        {
            var model = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT") ?? "gpt-4.1";
            settings["Llm:AzureOpenAi:ApiKey"] = Environment.GetEnvironmentVariable(AzureKey)!;
            settings["Llm:AzureOpenAi:Endpoint"] = Environment.GetEnvironmentVariable(AzureEndpoint)!;
            settings["Llm:AzureOpenAi:Model"] = model;
            foreach (var agent in new[] { "Requirement", "Coding", "Testing", "Qa" })
            {
                settings[$"Agents:{agent}:Provider"] = "AzureOpenAI";
                settings[$"Agents:{agent}:Model"] = model;
                settings[$"Agents:{agent}:Temperature"] = "0.1";
                settings[$"Agents:{agent}:MaxTokens"] = agent == "Coding" ? "3000" : "1500";
            }
        }
        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    private static void SkipIfNotEnabled(string provider)
    {
        if (Environment.GetEnvironmentVariable(RunFlag) != "1")
        {
            Assert.Skip($"{RunFlag} != 1 — set to 1 to run the live pipeline (cost ≤ ${MaxBudgetUsd}/run).");
        }
        if (string.Equals(provider, "Claude", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AnthropicKey)))
            {
                Assert.Skip($"{AnthropicKey} not set.");
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AzureKey))
                || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AzureEndpoint)))
            {
                Assert.Skip($"{AzureKey} or {AzureEndpoint} not set.");
            }
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
