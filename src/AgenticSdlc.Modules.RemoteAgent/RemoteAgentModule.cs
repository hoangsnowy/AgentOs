// Module entry: registers the broker + approver + Llm provider, wires SignalR, and mounts the
// /hubs/remote-agent endpoint. Hooks the dispatch transport as a hosted service.

using System;
using AgenticSdlc.Domain.Llm;
using AgenticSdlc.SharedKernel.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgenticSdlc.Modules.RemoteAgent;

public sealed class RemoteAgentModule : IModule, IEndpointModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IRemoteAgentBroker, InProcessRemoteAgentBroker>();
        services.TryAddSingleton<IRemoteExecApprover, AutoApproveRemoteExec>();

        // Register the Llm provider as a keyed ILlmClient so Llm.LlmClientFactory can resolve it by name.
        services.AddKeyedTransient<ILlmClient, RemoteAgentLlmClient>("RemoteAgent");

        services.AddSignalR();
        services.AddHostedService<RemoteAgentTransport>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapHub<RemoteAgentHub>(RemoteAgentHub.Path);
    }
}
