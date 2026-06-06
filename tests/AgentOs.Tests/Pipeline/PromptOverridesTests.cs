// Runtime per-tenant prompt overrides: the resolver the agents consult (override when set, default
// otherwise) + the tenant-explicit read/write/clear service.

using System.Threading.Tasks;
using AgentOs.Modules.AppConfig;
using AgentOs.Modules.Pipeline.Prompts;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Pipeline;

public sealed class PromptOverridesTests
{
    private static AppConfigPromptOverrides Resolver(IAppConfigStore? store) =>
        new(NullLogger<AppConfigPromptOverrides>.Instance, store);

    [Fact]
    public async Task Resolve_NoOverride_ReturnsDefault()
    {
        var resolver = Resolver(new InMemoryAppConfigStore());
        (await resolver.ResolveAsync("Requirement", "DEFAULT")).ShouldBe("DEFAULT");
    }

    [Fact]
    public async Task Resolve_NoConfigStore_ReturnsDefault()
    {
        var resolver = Resolver(store: null);
        (await resolver.ResolveAsync("Requirement", "DEFAULT")).ShouldBe("DEFAULT");
    }

    [Fact]
    public async Task Resolve_OverrideSet_ReturnsOverride()
    {
        var store = new InMemoryAppConfigStore();
        // The resolver reads the ambient tenant; with no AmbientIdentity pushed it is "default".
        await store.SetForTenantAsync("default", AppConfigPromptOverrides.Key("Coding"), "CUSTOM PROMPT");
        var resolver = Resolver(store);

        (await resolver.ResolveAsync("Coding", "DEFAULT")).ShouldBe("CUSTOM PROMPT");
    }

    [Fact]
    public async Task Service_Set_Get_Clear_RoundTrips()
    {
        var store = new InMemoryAppConfigStore();
        var service = new PromptOverrideService(store);

        (await service.GetAsync("t1", "Qa")).ShouldBeNull();

        await service.SetAsync("t1", "Qa", "tuned qa prompt");
        (await service.GetAsync("t1", "Qa")).ShouldBe("tuned qa prompt");

        await service.ClearAsync("t1", "Qa");
        (await service.GetAsync("t1", "Qa")).ShouldBeNull();
    }

    [Fact]
    public async Task Service_SetBlank_ClearsOverride()
    {
        var store = new InMemoryAppConfigStore();
        var service = new PromptOverrideService(store);
        await service.SetAsync("t1", "Testing", "something");

        await service.SetAsync("t1", "Testing", "   ");

        (await service.GetAsync("t1", "Testing")).ShouldBeNull();
    }
}
