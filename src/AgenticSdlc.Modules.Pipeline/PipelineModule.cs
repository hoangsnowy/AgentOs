// Module entry: registers the agents, prompts, validation, metrics, orchestrator, pipeline client,
// repositories, and DbContext. Mounts /pipeline* + /runs* endpoints. Applies the EF migration at
// startup. When no DB is configured, repos fall back to no-ops so the host still boots.

using System;
using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Modules.Pipeline.Agents;
using AgenticSdlc.Modules.Pipeline.Endpoints;
using AgenticSdlc.Modules.Pipeline.Metrics;
using AgenticSdlc.Modules.Pipeline.Persistence;
using AgenticSdlc.Modules.Pipeline.Persistence.Repositories;
using AgenticSdlc.Modules.Pipeline.Pipeline;
using AgenticSdlc.Modules.Pipeline.Validation;
using AgenticSdlc.SharedKernel.Identity;
using AgenticSdlc.SharedKernel.Modularity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgenticSdlc.Modules.Pipeline;

public sealed class PipelineModule : IModule, IEndpointModule, IInitializableModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddValidation();

        var csvPath = configuration["Metrics:CsvPath"];
        if (!string.IsNullOrWhiteSpace(csvPath))
        {
            services.AddCsvMetrics(csvPath);
        }
        else
        {
            services.AddInMemoryMetrics();
        }

        services.AddAgents(configuration);
        services.AddInProcessPipelineClient();

        // Persistence: real EF + Postgres when a connection string is set; otherwise no-op repos so
        // the host still boots stateless (Persistence:RequireDatabase=false opts into the legacy path).
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContext<PipelineDbContext>(opt =>
                opt.UseNpgsql(connectionString, npg =>
                    npg.MigrationsHistoryTable("__EFMigrationsHistory", schema: "pipeline")));
            services.AddScoped<IPipelineRunRepository, PipelineRunRepository>();
            services.AddScoped<IOrchestrationRepository, OrchestrationRepository>();
        }
        else
        {
            var requireDb = configuration.GetValue("Persistence:RequireDatabase", true);
            if (requireDb)
            {
                throw new InvalidOperationException(
                    "ConnectionStrings:DefaultConnection is empty but Persistence:RequireDatabase is true. "
                    + "Run the AppHost (dotnet run --project src/AgenticSdlc.AppHost) so Aspire wires Postgres, "
                    + "or set Persistence:RequireDatabase=false to opt into the legacy no-op repositories.");
            }
            services.AddSingleton<IPipelineRunRepository, NullPipelineRunRepository>();
            services.AddSingleton<IOrchestrationRepository, NullOrchestrationRepository>();
        }

        services.TryAddSingleton<IAuthTokenProvider>(NullAuthTokenProvider.Instance);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapPipelineEndpoints();
    }

    public async Task InitializeAsync(IServiceProvider services, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetService<PipelineDbContext>();
        if (db is not null)
        {
            await db.Database.MigrateAsync(ct).ConfigureAwait(false);
        }
    }
}
