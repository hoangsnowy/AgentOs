// Module variant that also maps HTTP endpoints. Implemented by modules that expose REST/SignalR
// routes (Pipeline, Identity, Tenants, AppConfig, RemoteAgent). ModuleLoader.MapModuleEndpoints
// resolves every IEndpointModule from DI after Build() and calls MapEndpoints.

using Microsoft.AspNetCore.Routing;

namespace AgenticSdlc.SharedKernel.Modularity;

/// <summary>A module that contributes HTTP endpoints.</summary>
public interface IEndpointModule : IModule
{
    /// <summary>Map the module's endpoints onto the application's route table.</summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
