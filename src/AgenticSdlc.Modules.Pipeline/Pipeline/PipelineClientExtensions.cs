// AgenticSdlc.Infrastructure/Pipeline/PipelineClientExtensions.cs
// Phase 8 — DI registration helpers for the two IPipelineClient impls.

using System;
using AgenticSdlc.SharedKernel.Identity;
using AgenticSdlc.Modules.Pipeline.Pipeline;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgenticSdlc.Modules.Pipeline.Pipeline;

/// <summary>DI extensions for wiring up the Phase 8 <see cref="IPipelineClient"/>.</summary>
public static class PipelineClientExtensions
{
    /// <summary>
    /// Registers the in-process pipeline client. Use on the API host (which itself owns the
    /// orchestrator). The scoped <see cref="MutableSinkHolder"/> is wired so the same orchestrator
    /// scope routes per-call sinks (the SSE endpoint swaps the inner sink before resolving the
    /// orchestrator).
    /// </summary>
    public static IServiceCollection AddInProcessPipelineClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        // Holder is scoped — one per request/scope.
        services.AddScoped<MutableSinkHolder>();
        // Replace the default null sink with the holder, so the orchestrator reports flow through it.
        services.AddScoped<IPipelineProgressSink>(sp => sp.GetRequiredService<MutableSinkHolder>());
        services.AddSingleton<IPipelineClient, InProcessPipelineClient>();
        return services;
    }

    /// <summary>
    /// Registers the HTTP pipeline client. Use on the Web host when it should call the API over the
    /// network. Reads <c>Api:BaseUrl</c> from configuration; falls back to <c>http://localhost:5080/</c>
    /// for dev mode. Authentication: the per-call bearer token is read from
    /// <see cref="IAuthTokenProvider"/> (registered by the host as <see cref="NullAuthTokenProvider"/>
    /// by default; the Web host overrides with a per-circuit <c>AuthSession</c> after login).
    /// </summary>
    public static IServiceCollection AddHttpPipelineClient(this IServiceCollection services, IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);
        var baseUrl = config["Api:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = "http://localhost:5080/";
        }
        if (!baseUrl.EndsWith('/'))
        {
            baseUrl += "/";
        }
        services.AddHttpClient(HttpPipelineClient.HttpClientName, c =>
        {
            c.BaseAddress = new Uri(baseUrl);
            c.Timeout = TimeSpan.FromMinutes(10); // pipelines can take minutes when NMax > 1
        });
        // Default auth provider — Web overrides with AuthSession after the user signs in.
        services.TryAddSingleton<IAuthTokenProvider>(NullAuthTokenProvider.Instance);
        services.AddScoped<IPipelineClient, HttpPipelineClient>();
        return services;
    }
}
