// Module entry: registers the tenant registry DbContext + repository + Keycloak admin client +
// /tenants endpoints. Active when a DB connection string is present; otherwise no-op repo so the
// host still boots.

using System;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Modules.Tenants.Email;
using AgentOs.Modules.Tenants.Endpoints;
using AgentOs.Modules.Tenants.Keycloak;
using AgentOs.Modules.Tenants.Persistence;
using AgentOs.Modules.Tenants.Persistence.Entities;
using AgentOs.Modules.Tenants.Persistence.Repositories;
using AgentOs.SharedKernel.Modularity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentOs.Modules.Tenants;

public sealed class TenantsModule : IModule, IEndpointModule, IInitializableModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<KeycloakAdminOptions>()
            .Bind(configuration.GetSection(KeycloakAdminOptions.SectionName));
        services.AddHttpClient<IKeycloakAdminClient, KeycloakAdminClient>();
        services.AddScoped<ITenantSignupService, TenantSignupService>();

        // App-sent email (invitation links). Real MailKit sender when an SMTP host is configured
        // (full stack injects MailHog; prod injects a real provider via secrets); otherwise a no-op
        // logger so standalone dev / CI still boot. Keycloak's own auth emails are separate.
        services.AddOptions<EmailOptions>().Bind(configuration.GetSection(EmailOptions.SectionName));
        var smtpHost = configuration.GetSection(EmailOptions.SectionName)["SmtpHost"];
        if (!string.IsNullOrWhiteSpace(smtpHost))
        {
            services.AddSingleton<IEmailSender, MailKitEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender, NullEmailSender>();
        }

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContext<TenantsDbContext>(opt =>
                opt.UseNpgsql(connectionString, npg =>
                    npg.MigrationsHistoryTable("__EFMigrationsHistory", schema: "tenants")));
            services.AddScoped<ITenantsRepository, TenantsRepository>();
            services.AddScoped<IAuditLog, EfAuditLog>();
        }
        else
        {
            services.TryAddSingleton<ITenantsRepository, NullTenantsRepository>();
            services.TryAddSingleton<IAuditLog, NullAuditLog>();
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

            // Seed the 'default' tenant so operator-mode invitations can always be accepted.
            // DefaultTenantContext uses tenant id "default" for single-tenant / dev operation;
            // invitations created in that mode embed "default" and would fail the existence
            // check in TenantSignupService without this row.
            if (!await db.Tenants.AnyAsync(t => t.Id == "default", ct).ConfigureAwait(false))
            {
                db.Tenants.Add(new TenantEntity
                {
                    Id = "default",
                    Name = "Default Workspace",
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                });
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }
    }
}
