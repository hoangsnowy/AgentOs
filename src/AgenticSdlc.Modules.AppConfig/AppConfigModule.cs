// Module entry: registers IAppConfigStore + AppConfigDbContext, mounts /settings endpoints, and
// applies the EF migration at startup. When no DB connection string is configured the store falls
// back to InMemoryAppConfigStore so the host still boots stateless.

using System;
using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Modules.AppConfig.Endpoints;
using AgenticSdlc.Modules.AppConfig.Persistence;
using AgenticSdlc.SharedKernel.Modularity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgenticSdlc.Modules.AppConfig;

public sealed class AppConfigModule : IModule, IEndpointModule, IInitializableModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContext<AppConfigDbContext>(opt =>
                opt.UseNpgsql(connectionString, npg =>
                    npg.MigrationsHistoryTable("__EFMigrationsHistory", schema: "config")));
            services.AddSingleton<IAppConfigStore, EfAppConfigStore>();
            return;
        }

        services.TryAddSingleton<IAppConfigStore, InMemoryAppConfigStore>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapSettingsEndpoints();
    }

    public async Task InitializeAsync(IServiceProvider services, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetService<AppConfigDbContext>();
        if (db is not null)
        {
            await db.Database.MigrateAsync(ct).ConfigureAwait(false);
        }
    }
}
