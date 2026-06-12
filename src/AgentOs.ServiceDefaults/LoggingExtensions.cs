// Shared logging shape for both hosts. Production emits single-line JSON (machine-shippable to
// ELK/Splunk/App Insights without a parsing layer); everywhere else keeps the human console format.
// Every request pushes tenant + traceId into the log scope so multi-tenant incidents can be
// filtered to one tenant and joined across services by trace id.

using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentOs.ServiceDefaults;

/// <summary>Console formatter + request log-context enrichment shared by the API + Web hosts.</summary>
public static class LoggingExtensions
{
    /// <summary>JSON console in Production (structured, shippable), human-readable simple console
    /// otherwise. Scopes are included in both so the per-request tenant/traceId enrichment lands.</summary>
    public static IHostApplicationBuilder AddAgentOsLogging(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (builder.Environment.IsProduction())
        {
            builder.Logging.AddJsonConsole(options =>
            {
                options.IncludeScopes = true;
                options.UseUtcTimestamp = true;
            });
        }
        else
        {
            builder.Logging.AddSimpleConsole(options =>
            {
                options.IncludeScopes = true;
                options.SingleLine = false;
                options.TimestampFormat = "HH:mm:ss.fff ";
            });
        }
        return builder;
    }

    /// <summary>Wraps every request in a log scope carrying <c>tenant</c> + <c>traceId</c> so every
    /// log line written during the request is attributable. Register AFTER auth (the tenant claim
    /// must be resolved).</summary>
    public static IApplicationBuilder UseAgentOsRequestLogContext(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            var logger = context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("AgentOs.RequestContext");
            var scope = new Dictionary<string, object?>
            {
                ["tenant"] = context.User?.FindFirst("tenant")?.Value ?? string.Empty,
                ["traceId"] = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier,
            };
            using (logger.BeginScope(scope))
            {
                await next(context).ConfigureAwait(false);
            }
        });
    }
}
