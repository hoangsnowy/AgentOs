// DI for the persistence layer. Phase 8.5 — Postgres required by default.
// Set Persistence:RequireDatabase=false (env var Persistence__RequireDatabase=false) to opt
// into the legacy in-memory no-op repos (tests + Codespaces without Docker only).
using AgenticSdlc.Application.Persistence;
using AgenticSdlc.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticSdlc.Infrastructure.Persistence;

/// <summary>DI extension for persistence (EF Core + Postgres).</summary>
public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the DbContext + repositories. Phase 8.5 behaviour:
    /// <list type="bullet">
    ///   <item>Connection string present → EF Core + Postgres repos.</item>
    ///   <item>Connection string empty AND <c>Persistence:RequireDatabase=false</c> → no-op repos (legacy).</item>
    ///   <item>Connection string empty AND <c>Persistence:RequireDatabase=true</c> (default) → fail fast.</item>
    /// </list>
    /// </summary>
    /// <exception cref="System.InvalidOperationException">
    /// Thrown when <c>ConnectionStrings:DefaultConnection</c> is empty and
    /// <c>Persistence:RequireDatabase</c> is not <c>false</c>.
    /// </exception>
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContext<AgenticSdlcDbContext>(opt => opt.UseNpgsql(connectionString));
            services.AddScoped<IPipelineRunRepository, PipelineRunRepository>();
            services.AddScoped<IOrchestrationRepository, OrchestrationRepository>();
            return services;
        }

        var requireDb = configuration.GetValue("Persistence:RequireDatabase", true);
        if (requireDb)
        {
            throw new System.InvalidOperationException(
                "ConnectionStrings:DefaultConnection is empty but Persistence:RequireDatabase is true. " +
                "Start Postgres (docker compose up -d) and configure the connection string, or set " +
                "Persistence:RequireDatabase=false to opt into the legacy no-op repositories.");
        }

        services.AddSingleton<IPipelineRunRepository, NullPipelineRunRepository>();
        services.AddSingleton<IOrchestrationRepository, NullOrchestrationRepository>();
        return services;
    }

    /// <summary>
    /// Apply EF migration at startup (no-op if persistence has no DB configured).
    /// Call after building the app: <c>await app.Services.InitializePersistenceAsync();</c>
    /// </summary>
    public static async Task InitializePersistenceAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetService<AgenticSdlcDbContext>();
        if (db is not null)
        {
            await db.Database.MigrateAsync(ct);
        }
    }
}
