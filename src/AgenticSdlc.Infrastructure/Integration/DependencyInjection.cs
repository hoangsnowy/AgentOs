// AgenticSdlc.Infrastructure/Integration/DependencyInjection.cs
// DI extension for third-party integration services (currently: GitHub PR opener).

using AgenticSdlc.Application.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticSdlc.Infrastructure.Integration;

/// <summary>DI registration for integration services (GitHub).</summary>
public static class IntegrationServiceCollectionExtensions
{
    /// <summary>Register <see cref="IGitHubPrService"/>. PAT + repo info live in <c>IRuntimeOverrides</c>.</summary>
    public static IServiceCollection AddGitHubIntegration(this IServiceCollection services)
    {
        services.AddTransient<IGitHubPrService, GitHubPrService>();
        return services;
    }
}
