// Regression guard for a topology bug that shipped on main: the Blazor Web host rendered Board's
// "Run on my machine" toggle, which sets IssueWorkRequest.ProviderOverride = "RemoteAgent", but the
// Web host did not load Modules.RemoteAgent — the only place the keyed ILlmClient "RemoteAgent" is
// registered. The session runs IN the Web process (BoardApp.RunSession -> Task.Run -> CreateScope),
// so LlmClientFactory threw "LLM provider 'RemoteAgent' … is not registered" the moment a member
// used the toggle. Compilation cannot catch this: the provider name is a string crossing a DI seam.

using System;
using System.Linq;
using AgentOs.Domain.Llm;
using AgentOs.SharedKernel.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Web;

public class WebHostRemoteAgentProviderTests
{
    // The provider name Board's "Run on my machine" toggle sends (BoardApp.razor, RunSession).
    private const string RunOnMyMachineProvider = "RemoteAgent";

    [Fact]
    public void WebHost_ReferencesRemoteAgentModuleAssembly()
    {
        var webAssembly = typeof(AgentOs.Web.Services.AppCatalog).Assembly;

        var referenced = webAssembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

        referenced.ShouldContain("AgentOs.Modules.RemoteAgent");
    }

    [Fact]
    public void ModuleSet_WithRemoteAgent_ResolvesRunOnMyMachineProvider()
    {
        using var sp = BuildWebLikeServices();
        var factory = sp.GetRequiredService<ILlmClientFactory>();

        var client = factory.Create(RunOnMyMachineProvider);

        client.Provider.ShouldBe(RunOnMyMachineProvider);
    }

    [Fact]
    public void ModuleSet_WithoutRemoteAgent_ThrowsForRunOnMyMachineProvider()
    {
        // Pins the failure mode the fix removes, so a future host that drops the module fails here
        // with a readable message rather than at runtime in a member's session.
        using var sp = BuildServices(
            typeof(AgentOs.Modules.Llm.LlmModule).Assembly,
            typeof(AgentOs.Modules.AppConfig.AppConfigModule).Assembly);
        var factory = sp.GetRequiredService<ILlmClientFactory>();

        var ex = Should.Throw<LlmException>(() => factory.Create(RunOnMyMachineProvider));

        ex.Message.ShouldContain(RunOnMyMachineProvider);
    }

    private static ServiceProvider BuildWebLikeServices() => BuildServices(
        typeof(AgentOs.Modules.Llm.LlmModule).Assembly,
        typeof(AgentOs.Modules.AppConfig.AppConfigModule).Assembly,
        typeof(AgentOs.Modules.RemoteAgent.RemoteAgentModule).Assembly);

    private static ServiceProvider BuildServices(params System.Reflection.Assembly[] moduleAssemblies)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // RemoteAgentLlmClient takes ITenantContext (it targets the caller's paired runner). In a host
        // that comes from the Identity module; here a stub keeps the test on the keyed-provider seam.
        services.AddSingleton<AgentOs.SharedKernel.Identity.ITenantContext, AgentOs.Tests.Identity.TestTenantContext>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
            {
                ["Llm:Provider"] = "Claude",
                ["Llm:Claude:ApiKey"] = "test",
                ["Llm:Claude:Endpoint"] = "https://api.anthropic.test",
            })
            .Build();
        services.AddModulesFromAssemblies(config, moduleAssemblies);
        return services.BuildServiceProvider();
    }
}
