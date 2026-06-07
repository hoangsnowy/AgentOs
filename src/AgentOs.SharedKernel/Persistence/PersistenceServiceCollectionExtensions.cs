using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentOs.SharedKernel.Persistence;

/// <summary>Registration for the shared <see cref="INpgsqlConnectionFactory"/>.</summary>
public static class PersistenceServiceCollectionExtensions
{
    /// <summary>Registers the Npgsql connection factory once (idempotent across modules via
    /// <c>TryAddSingleton</c>). No-ops on a blank connection string, so the no-DB boot path stays
    /// EF/no-op only and repositories see a null factory → EF fallback.</summary>
    public static IServiceCollection AddNpgsqlConnectionFactory(this IServiceCollection services, string? connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.TryAddSingleton<INpgsqlConnectionFactory>(new NpgsqlConnectionFactory(connectionString));
        }

        return services;
    }
}
