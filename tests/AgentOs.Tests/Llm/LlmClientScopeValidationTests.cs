// Regression: the keyed-singleton ILlmClient (Claude/AzureOpenAI) must NOT capture a scoped ITenantContext
// at construction. It used to call sp.GetService<ITenantContext>() in the singleton factory, which throws
// "Cannot resolve scoped service 'ITenantContext' from root provider" under scope validation when the host
// registers a SCOPED tenant context (e.g. Keycloak's HttpTenantContext on the full stack). The Quality
// pipeline run was the first path to build a server-side LLM client there. The fix passes the root provider
// and resolves the tenant per-call, so resolving the singleton no longer touches the scoped service.

using AgentOs.Domain.Llm;
using AgentOs.Modules.Llm;
using AgentOs.SharedKernel.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Llm;

public sealed class LlmClientScopeValidationTests
{
    private sealed class ScopedTenant : ITenantContext
    {
        public string TenantId => "t1";
        public string? UserId => "u1";
        public string? UserName => "u1";
        public IReadOnlyList<string> Roles { get; } = new[] { "admin" };
        public bool IsAuthenticated => true;
        public bool IsAdmin => true;
    }

    [Theory]
    [InlineData("Claude")]
    [InlineData("AzureOpenAI")]
    public void ResolveKeyedSingletonClient_WithScopedTenantContext_AndScopeValidation_DoesNotThrow(string provider)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // The trigger condition: a SCOPED tenant context (mirrors HttpTenantContext on the Keycloak stack).
        services.AddScoped<ITenantContext, ScopedTenant>();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        new LlmModule().AddServices(services, config);

        // ValidateScopes = true makes resolving a scoped service from the root provider throw — exactly the
        // failure the Quality run hit. The keyed clients are singletons, so their factory runs against root.
        using var sp = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var client = Should.NotThrow(() => sp.GetRequiredKeyedService<ILlmClient>(provider));
        client.ShouldNotBeNull();
        client.Provider.ShouldBe(provider);
    }
}
