// Module contract — every Modules.* assembly exposes one concrete IModule that wires its DI in
// AddServices. The host calls ModuleLoader.AddModulesFromAssemblies to discover and invoke them all.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticSdlc.SharedKernel.Modularity;

/// <summary>A discoverable module that contributes services to the host's DI container.</summary>
public interface IModule
{
    /// <summary>Register the module's services. Called once at host startup.</summary>
    void AddServices(IServiceCollection services, IConfiguration configuration);
}
