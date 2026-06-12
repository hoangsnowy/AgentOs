// Batch 2 — fail-fast configuration: every module's options validate at startup, so a bad value
// aborts boot with a named error instead of failing mid-run. Accessing IOptions<T>.Value triggers
// the same Validate chain ValidateOnStart runs at host start.
using AgentOs.Modules.Mcp;
using AgentOs.Modules.Mcp.Configuration;
using AgentOs.Modules.Pipeline.Agents;
using AgentOs.Modules.Pipeline.Pipeline;
using AgentOs.Modules.Tenants;
using AgentOs.Modules.Tenants.Email;
using AgentOs.Modules.Tenants.Keycloak;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Configuration;

public class OptionsValidationTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)))
            .Build();

    private static T Resolve<T>(System.Action<IServiceCollection, IConfiguration> addServices, IConfiguration config)
        where T : class
    {
        var services = new ServiceCollection();
        addServices(services, config);
        using var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IOptions<T>>().Value;
    }

    // ── Agents + Pipeline (Pipeline module) ──────────────────────────────────────────────────

    private static void AddAgents(IServiceCollection s, IConfiguration c) => s.AddAgents(c);

    [Fact]
    public void AgentsOptions_Defaults_AreValid() =>
        Resolve<AgentsOptions>(AddAgents, Config()).ShouldNotBeNull();

    [Fact]
    public void AgentsOptions_TemperatureOutOfRange_FailsValidation() =>
        Should.Throw<OptionsValidationException>(() =>
            Resolve<AgentsOptions>(AddAgents, Config(("Agents:Coding:Temperature", "3.5"))));

    [Fact]
    public void AgentsOptions_EmptyModel_FailsValidation() =>
        Should.Throw<OptionsValidationException>(() =>
            Resolve<AgentsOptions>(AddAgents, Config(("Agents:Qa:Model", " "))));

    [Fact]
    public void PipelineOptions_ZeroMaxIterations_FailsValidation() =>
        Should.Throw<OptionsValidationException>(() =>
            Resolve<PipelineOptions>(AddAgents, Config(("Pipeline:MaxIterations", "0"))));

    [Fact]
    public void PipelineOptions_UnknownEngine_FailsValidation() =>
        Should.Throw<OptionsValidationException>(() =>
            Resolve<PipelineOptions>(AddAgents, Config(("Pipeline:Engine", "Quantum"))));

    [Fact]
    public void PipelineOptions_WorkflowEngine_IsValid() =>
        Resolve<PipelineOptions>(AddAgents, Config(("Pipeline:Engine", "workflow")))
            .Engine.ShouldBe("workflow");

    // ── MCP module ───────────────────────────────────────────────────────────────────────────

    private static void AddMcp(IServiceCollection s, IConfiguration c) => new McpModule().AddServices(s, c);

    [Fact]
    public void McpOptions_EnabledServerWithoutName_FailsValidation() =>
        Should.Throw<OptionsValidationException>(() =>
            Resolve<McpOptions>(AddMcp, Config(("Mcp:Servers:0:Transport", "stdio"), ("Mcp:Servers:0:Command", "npx"))));

    [Fact]
    public void McpOptions_StdioServerWithoutCommand_FailsValidation() =>
        Should.Throw<OptionsValidationException>(() =>
            Resolve<McpOptions>(AddMcp, Config(("Mcp:Servers:0:Name", "gh"), ("Mcp:Servers:0:Transport", "stdio"))));

    [Fact]
    public void McpOptions_HttpServerWithBadUrl_FailsValidation() =>
        Should.Throw<OptionsValidationException>(() =>
            Resolve<McpOptions>(AddMcp, Config(
                ("Mcp:Servers:0:Name", "gh"),
                ("Mcp:Servers:0:Transport", "http"),
                ("Mcp:Servers:0:Url", "not a url"))));

    [Fact]
    public void McpOptions_DisabledServer_SkipsValidation() =>
        Resolve<McpOptions>(AddMcp, Config(
            ("Mcp:Servers:0:Name", ""),
            ("Mcp:Servers:0:Transport", "stdio"),
            ("Mcp:Servers:0:Enabled", "false"))).ShouldNotBeNull();

    [Fact]
    public void McpOptions_ValidStdioServer_Passes() =>
        Resolve<McpOptions>(AddMcp, Config(
            ("Mcp:Servers:0:Name", "gh"),
            ("Mcp:Servers:0:Transport", "stdio"),
            ("Mcp:Servers:0:Command", "npx"))).Servers.Count.ShouldBe(1);

    // ── Tenants module (KeycloakAdmin + Email) ───────────────────────────────────────────────

    private static void AddTenants(IServiceCollection s, IConfiguration c) => new TenantsModule().AddServices(s, c);

    [Fact]
    public void KeycloakAdminOptions_MalformedBaseUrl_FailsValidation() =>
        Should.Throw<OptionsValidationException>(() =>
            Resolve<KeycloakAdminOptions>(AddTenants, Config(("Auth:Keycloak:Admin:BaseUrl", "not a url"))));

    [Fact]
    public void KeycloakAdminOptions_EmptyBaseUrl_IsValid() =>
        Resolve<KeycloakAdminOptions>(AddTenants, Config()).BaseUrl.ShouldBe(string.Empty);

    [Fact]
    public void EmailOptions_PortOutOfRange_FailsValidation() =>
        Should.Throw<OptionsValidationException>(() =>
            Resolve<EmailOptions>(AddTenants, Config(("Email:SmtpPort", "0"))));

    [Fact]
    public void EmailOptions_SmtpHostWithoutFrom_FailsValidation() =>
        Should.Throw<OptionsValidationException>(() =>
            Resolve<EmailOptions>(AddTenants, Config(("Email:SmtpHost", "smtp.example.com"), ("Email:From", " "))));

    [Fact]
    public void EmailOptions_Defaults_AreValid() =>
        Resolve<EmailOptions>(AddTenants, Config()).SmtpPort.ShouldBe(1025);
}
