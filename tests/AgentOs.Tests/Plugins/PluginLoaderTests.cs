// AgentOs.Tests/Plugins/PluginLoaderTests.cs
// The runtime plugin loader: discovers IAgentOsPlugin types in an assembly, runs their AddServices,
// registers them as modules, summarises capabilities, and records the outcome in IPluginCatalog. A
// plugin that throws is recorded as Failed without aborting discovery of the others.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Tools;
using AgentOs.SharedKernel.Modularity;
using AgentOs.SharedKernel.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Plugins;

public sealed class PluginLoaderTests
{
    private sealed class FakeTool : ITool
    {
        public ToolDefinition Definition { get; } = new("fake_tool", "test tool", "{}");

        public Task<ToolInvocationResult> InvokeAsync(ToolInvocationRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(ToolInvocationResult.Success("id", "ok"));
    }

    // Two IAgentOsPlugin implementations in this (test) assembly — discovered by the loader when it
    // scans typeof(GoodPlugin).Assembly.
    public sealed class GoodPlugin : IAgentOsPlugin
    {
        public PluginManifest Manifest { get; } = new("test.good", "Good Plugin", "1.0.0", "tester", "registers a tool");

        public void AddServices(IServiceCollection services, IConfiguration configuration)
            => services.AddSingleton<ITool, FakeTool>();
    }

    public sealed class ThrowingPlugin : IAgentOsPlugin
    {
        public PluginManifest Manifest { get; } = new("test.throwing", "Throwing Plugin", "0.1.0");

        public void AddServices(IServiceCollection services, IConfiguration configuration)
            => throw new InvalidOperationException("boom");
    }

    private static IConfiguration EmptyConfig() => new ConfigurationBuilder().Build();

    [Fact]
    public void AddPluginsFromAssemblies_DiscoversPlugin_RegistersToolAndModule_RecordsCatalog()
    {
        var services = new ServiceCollection();

        services.AddPluginsFromAssemblies(EmptyConfig(), [typeof(GoodPlugin).Assembly]);

        var sp = services.BuildServiceProvider();
        var good = sp.GetRequiredService<IPluginCatalog>().Plugins.FirstOrDefault(p => p.Manifest.Id == "test.good");

        good.ShouldNotBeNull();
        good!.Status.ShouldBe(PluginStatus.Loaded);
        good.Manifest.Version.ShouldBe("1.0.0");
        good.Capabilities.ShouldContain("1 tool");
        sp.GetServices<IModule>().OfType<GoodPlugin>().ShouldNotBeEmpty(); // wired as a module
        sp.GetServices<ITool>().OfType<FakeTool>().ShouldNotBeEmpty();     // its tool reachable in DI
    }

    [Fact]
    public void AddPluginsFromAssemblies_PluginThrows_RecordedAsFailed_OthersStillLoad()
    {
        var services = new ServiceCollection();

        services.AddPluginsFromAssemblies(EmptyConfig(), [typeof(ThrowingPlugin).Assembly]);

        var catalog = services.BuildServiceProvider().GetRequiredService<IPluginCatalog>();
        catalog.Plugins.ShouldContain(p => p.Manifest.Id == "test.throwing" && p.Status == PluginStatus.Failed && p.Error!.Contains("boom"));
        catalog.Plugins.ShouldContain(p => p.Manifest.Id == "test.good" && p.Status == PluginStatus.Loaded);
    }

    [Fact]
    public void AddPlugins_MissingFolder_IsNoOp_EmptyCatalog()
    {
        var services = new ServiceCollection();
        var missing = Path.Combine(Path.GetTempPath(), "agentos-no-plugins-" + Guid.NewGuid().ToString("N"));

        services.AddPlugins(EmptyConfig(), missing);

        services.BuildServiceProvider().GetRequiredService<IPluginCatalog>().Plugins.ShouldBeEmpty();
    }
}
