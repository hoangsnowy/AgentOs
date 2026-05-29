// Module entry: registers the tenant registry DbContext + repository + Keycloak admin client +
// /tenants endpoints. Active when a DB connection string is present; otherwise no-op repo so the
// host still boots.

using System;
using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Modules.Tenants.Endpoints;
using AgenticSdlc.Modules.Tenants.Keycloak;
using AgenticSdlc.Modules.Tenants.Persistence;
using AgenticSdlc.Modules.Tenants.Persistence.Repositories;
using AgenticSdlc.SharedKernel.Modularity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgenticSdlc.Modules.Tenants;

public sealed class TenantsModule : IModule, IEndpointModule, IInitializableModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<KeycloakAdminOptions>()
            .Bind(configuration.GetSection(KeycloakAdminOptions.SectionName));
        services.AddHttpClient<IKeycloakAdminClient, KeycloakAdminClient>();

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContext<TenantsDbContext>(opt =>
                opt.UseNpgsql(connectionString, npg =>
                    npg.MigrationsHistoryTable("__EFMigrationsHistory", schema: "tenants")));
            services.AddScoped<ITenantsRepository, TenantsRepository>();
        }
        else
        {
            services.TryAddSingleton<ITenantsRepository, NullTenantsRepository>();
        }
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapTenantEndpoints();
    }

    public async Task InitializeAsync(IServiceProvider services, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetService<TenantsDbContext>();
        if (db is not null)
        {
            await db.Database.MigrateAsync(ct).ConfigureAwait(false);
        }
    }
}
