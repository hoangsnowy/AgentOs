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

    [Fact]
    public void Create_PluginRegisteredProvider_ResolvesByItsOwnKey()
    {
        // A plugin registers a keyed ILlmClient under a name the built-in NormalizeKey doesn't know;
        // the factory must fall through to that exact key instead of throwing "unknown provider".
        var services = new ServiceCollection();
        services.AddLogging();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?> { ["Llm:Provider"] = "Echo" })
            .Build();
        services.AddModulesFromAssemblies(config, typeof(LlmModule).Assembly, typeof(AgentOs.Modules.AppConfig.AppConfigModule).Assembly);
        services.AddKeyedSingleton<ILlmClient>("Echo", (_, _) => new EchoClient());
        var factory = services.BuildServiceProvider().GetRequiredService<ILlmClientFactory>();

        factory.Create("Echo").Provider.ShouldBe("Echo");
        factory.CreateDefault().Provider.ShouldBe("Echo");
    }

    [Fact]
    public async Task Create_WithFallbackConfig_ComposesChain_AndFailsOverWhenPrimaryThrows()
    {
        // Llm:Fallbacks maps the (plugin) primary "P1" to fallback "P2". The factory must compose a
        // FailoverLlmClient that reports the primary but routes the call to P2 once P1 throws.
        var services = new ServiceCollection();
        services.AddLogging();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
            {
                ["Llm:Provider"] = "P1",
                ["Llm:Fallbacks:P1:0"] = "P2",
            })
            .Build();
        services.AddModulesFromAssemblies(config, typeof(LlmModule).Assembly, typeof(AgentOs.Modules.AppConfig.AppConfigModule).Assembly);
        services.AddKeyedSingleton<ILlmClient>("P1", (_, _) => new ThrowingClient("P1"));
        services.AddKeyedSingleton<ILlmClient>("P2", (_, _) => new EchoClient("P2"));
        var factory = services.BuildServiceProvider().GetRequiredService<ILlmClientFactory>();

        var client = factory.Create("P1");
        client.Provider.ShouldBe("P1"); // reports the primary

        var resp = await client.SendAsync(new LlmRequest("s", "u", "m", 0.0, 10));
        resp.Provider.ShouldBe("P2"); // failed over to the fallback
    }

    [Fact]
    public void Create_WithoutFallbackConfig_ReturnsBareClient()
    {
        var sp = BuildServices("Claude");
        var factory = sp.GetRequiredService<ILlmClientFactory>();

        // No Llm:Fallbacks configured → the resolved client is the provider's own client, not a wrapper.
        factory.Create("Claude").ShouldBeOfType<PooledChatLlmClient>();
    }

    private sealed class ThrowingClient(string provider) : ILlmClient
    {
        public string Provider { get; } = provider;

        public System.Threading.Tasks.Task<LlmResponse> SendAsync(
            LlmRequest request, System.Threading.CancellationToken cancellationToken = default)
            => throw new LlmException($"{Provider} exhausted", Provider);
    }

    private sealed class EchoClient : ILlmClient
    {
        public EchoClient(string provider = "Echo") => Provider = provider;

        public string Provider { get; }

        public System.Threading.Tasks.Task<LlmResponse> SendAsync(
            LlmRequest request, System.Threading.CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(
                new LlmResponse(string.Empty, 0, 0, 0m, System.TimeSpan.Zero, "echo-model", Provider));
    }
}
