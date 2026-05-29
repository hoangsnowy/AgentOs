// Module variant that runs an async startup hook — typically EF Core migration apply or warm-up.
// ModuleLoader.InitializeModulesAsync resolves every IInitializableModule and awaits each in
// registration order (modules with downstream dependencies should declare them in order in the host).

using System;
using System.Threading;
using System.Threading.Tasks;

namespace AgenticSdlc.SharedKernel.Modularity;

/// <summary>A module that performs async work at host startup (e.g. apply DB migrations).</summary>
public interface IInitializableModule : IModule
{
    /// <summary>Run the module's startup hook. Called once after the app is built.</summary>
    Task InitializeAsync(IServiceProvider services, CancellationToken ct);
}
