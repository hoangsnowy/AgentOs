using AgenticSdlc.SharedKernel.Modularity;
// AgenticSdlc.Tests/Llm/LlmClientFactoryTests.cs
// Sprint 1 — Unit tests for LlmClientFactory (resolve by provider name).

using System;
using AgenticSdlc.Domain.Llm;
using AgenticSdlc.Modules.Llm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AgenticSdlc.Tests.Llm;

public class LlmClientFactoryTests
{
    private static IServiceProvider BuildServices(string defaultProvider)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
            {
                ["Llm:Provider"] = defaultProvider,
                ["Llm:Mock:FixturePath"] = "tests/fixtures/llm",
                ["Llm:Mock:SimulatedLatencyMs"] = "0",
                ["Llm:Claude:ApiKey"] = "test",
                ["Llm:Claude:Endpoint"] = "https://api.anthropic.test",
                ["Llm:AzureOpenAi:ApiKey"] = "test",
                ["Llm:AzureOpenAi:Endpoint"] = "https://test.openai.azure.com",
            })
            .Build();
        services.AddModulesFromAssemblies(config, typeof(AgenticSdlc.Modules.Llm.LlmModule).Assembly, typeof(AgenticSdlc.Modules.AppConfig.AppConfigModule).Assembly);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Create_Claude_ReturnsClaudeProvider()
    {
        var sp = BuildServices("Mock");
        var factory = sp.GetRequiredService<ILlmClientFactory>();

        var client = factory.Create("Claude");

        // SDK-based: a pooled IChatClient wrapper, identified by its provider tag.
        client.Provider.ShouldBe("Claude");
    }

    [Fact]
    public void Create_AzureOpenAI_ReturnsAzureProvider()
    {
        var sp = BuildServices("Mock");
        var factory = sp.GetRequiredService<ILlmClientFactory>();

        var client = factory.Create("AzureOpenAI");

        client.Provider.ShouldBe("AzureOpenAI");
    }

    [Fact]
    public void Create_Mock_ReturnsMockClient()
    {
        var sp = BuildServices("Mock");
        var factory = sp.GetRequiredService<ILlmClientFactory>();

        var client = factory.Create("Mock");

        client.ShouldBeOfType<MockLlmClient>();
    }

    [Fact]
    public void CreateDefault_FromConfig_ReturnsCorrectProvider()
    {
        var sp = BuildServices("Claude");
        var factory = sp.GetRequiredService<ILlmClientFactory>();

        var client = factory.CreateDefault();

        client.Provider.ShouldBe("Claude");
    }

    [Fact]
    public void Create_UnknownProvider_ThrowsLlmException()
    {
        var sp = BuildServices("Mock");
        var factory = sp.GetRequiredService<ILlmClientFactory>();

        Should.Throw<LlmException>(() => factory.Create("Bedrock"));
    }
}
