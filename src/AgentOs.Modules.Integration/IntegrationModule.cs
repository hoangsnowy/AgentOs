// Module entry: registers IGitHubPrService + IBuildVerifier. Both consumed by Pipeline orchestrator
// when the run generates code that needs to land in a PR or be build-verified locally first.

using System;
using AgentOs.Domain.Workspaces;
using AgentOs.Modules.Integration.Sources;
using AgentOs.Modules.Integration.Tools;
using AgentOs.Modules.Tools;
using AgentOs.SharedKernel.Modularity;
using AgentOs.SharedKernel.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgentOs.Modules.Integration;

public sealed class IntegrationModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddGitHubIntegration();
        // build_verifier runs LLM-generated MSBuild projects (arbitrary code execution) — off by default in
        // Production until a sandboxed runner exists; Development defaults on. Config key overrides.
        services.AddSingleton(new BuildVerifierOptions(
            configuration.GetValue("Integration:BuildVerifier:Enabled", !ModuleEnvironment.IsProduction(configuration))));
        services.AddBuildVerifier();
        services.AddTransient<IBoardTicketService, BoardTicketService>();
        services.AddTransient<IBoardWriteService, BoardWriteService>();
        services.AddTransient<AgentOs.Domain.Workspaces.IPrCreationService, GitHubPrCreationService>();
        // Epic E2 — wrap IBuildVerifier as an ITool so agents can call it via FunctionInvokingChatClient.
        // ToolsModule.InitializeAsync pumps every ITool DI registration into the IToolRegistry at startup.
        services.AddTool<BuildVerifierTool>();

        // M2 — source-control providers behind one seam (GitHub + Azure DevOps both live for repos;
        // ADO boards/work-items are a later milestone) + the by-kind resolver the Workspaces module consumes.
        services.AddHttpClient(); // IHttpClientFactory for the Azure DevOps REST provider
        // The ADO REST provider talks to a TENANT-SUPPLIED host → SSRF risk. Route its named client through
        // the SSRF-hardened handler that refuses to connect to private/loopback/link-local IPs (incl. via
        // DNS + redirects). The provider resolves this client by name (see AzureDevOpsSourceProvider.Client).
        services.AddHttpClient(nameof(AzureDevOpsSourceProvider))
            .ConfigurePrimaryHttpMessageHandler(SsrfGuard.CreateHardenedHandler);
        services.AddSingleton<ISourceProvider, GitHubSourceProvider>();
        services.AddSingleton<ISourceProvider, AzureDevOpsSourceProvider>();
        services.AddSingleton<ISourceProviderResolver, SourceProviderResolver>();
    }
}
