using AgentOs.SharedKernel.Modularity;
using System;
using AgentOs.Domain.Llm;
using AgentOs.Modules.Llm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Llm;

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
                ["Llm:Claude:ApiKey"] = "test",
                ["Llm:Claude:Endpoint"] = "https://api.anthropic.test",
                ["Llm:AzureOpenAi:ApiKey"] = "test",
                ["Llm:AzureOpenAi:Endpoint"] = "https://test.openai.azure.com",
            })
            .Build();
        services.AddModulesFromAssemblies(config, typeof(AgentOs.Modules.Llm.LlmModule).Assembly, typeof(AgentOs.Modules.AppConfig.AppConfigModule).Assembly);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Create_Claude_ReturnsClaudeProvider()
    {
        var sp = BuildServices("Claude");
        var factory = sp.GetRequiredService<ILlmClientFactory>();

        var client = factory.Create("Claude");

        client.Provider.ShouldBe("Claude");
    }

    [Fact]
    public void Create_AzureOpenAI_ReturnsAzureProvider()
    {
        var sp = BuildServices("Claude");
        var factory = sp.GetRequiredService<ILlmClientFactory>();

        var client = factory.Create("AzureOpenAI");

        client.Provider.ShouldBe("AzureOpenAI");
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
        var sp = BuildServices("Claude");
        var factory = sp.GetRequiredService<ILlmClientFactory>();

        Should.Throw<LlmException>(() => factory.Create("Bedrock"));
    }
}
