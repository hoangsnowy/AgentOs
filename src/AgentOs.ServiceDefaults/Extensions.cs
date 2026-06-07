// Aspire service defaults: OpenTelemetry, health checks, service discovery, HTTP resilience.
// Shared by AgentOs.Api and AgentOs.Web; wired by the AppHost.
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace AgentOs.ServiceDefaults;

/// <summary>Aspire-compatible defaults shared by the API + Web hosts.</summary>
public static class Extensions
{
    /// <summary>Adds OpenTelemetry, default health checks, service discovery, HTTP client resilience,
    /// and forwarded-headers handling (for TLS-terminating reverse proxies / Container Apps ingress).</summary>
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        // Behind Azure Container Apps (and any TLS-terminating ingress) the app receives plain HTTP with
        // X-Forwarded-Proto: https. Without honouring it the OIDC middleware builds an http:// redirect_uri
        // (Keycloak then rejects it) and CookieSecurePolicy.Always drops the auth cookie. The proxy IP is
        // not known ahead of time in ACA, so the default KnownNetworks/KnownProxies allow-list (loopback
        // only) would discard the headers — clear it and trust the single hop in front of us.
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    /// <summary>Applies forwarded headers as the FIRST middleware so every downstream component (OIDC,
    /// cookie policy, link generation) sees the original https scheme + client IP. Call before any auth.</summary>
    public static WebApplication UseAgentOsForwardedHeaders(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseForwardedHeaders();
        return app;
    }

    /// <summary>Configures OpenTelemetry logging, metrics, and tracing (+ OTLP export when configured).</summary>
    public static IHostApplicationBuilder ConfigureOpenTelemetry(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter("AgentOs.Llm")) // SharedKernel.Telemetry.LlmTelemetry.SourceName — kept literal to avoid coupling this Aspire-shared project.
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSource("AgentOs.Llm"));

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    /// <summary>Adds a "self" liveness health check.</summary>
    public static IHostApplicationBuilder AddDefaultHealthChecks(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

        return builder;
    }

    /// <summary>Maps /health (readiness — all checks) and /alive (liveness — live-tagged checks).
    /// Mapped UNCONDITIONALLY: Container Apps / k8s liveness+readiness probes must work in Production,
    /// not just Development. (The hosts currently hand-roll equivalent /health + /alive routes; adopt
    /// this helper to centralise them once the per-host /health payloads are reconciled.)</summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapHealthChecks("/health");
        app.MapHealthChecks("/alive", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live"),
        });

        return app;
    }
}
