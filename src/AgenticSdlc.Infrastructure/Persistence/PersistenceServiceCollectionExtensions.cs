// DI for the persistence layer. With a connection string → EF Core + Postgres repos.
// Without one → null-object repos (app runs stateless, suitable for CI/local without a DB).
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
    /// Registers the DbContext + repositories. If <c>ConnectionStrings:DefaultConnection</c> is empty
    /// → use no-op repos so the app can still boot without a DB.
    /// </summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddSingleton<IPipelineRunRepository, NullPipelineRunRepository>();
            services.AddSingleton<IOrchestrationRepository, NullOrchestrationRepository>();
            return services;
        }

        services.AddDbContext<AgenticSdlcDbContext>(opt => opt.UseNpgsql(connectionString));
        services.AddScoped<IPipelineRunRepository, PipelineRunRepository>();
        services.AddScoped<IOrchestrationRepository, OrchestrationRepository>();
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
