// AgenticSdlc.Infrastructure/Metrics/DependencyInjection.cs
// Sprint 4 — DI for IMetricsCollector.

using System;
using AgenticSdlc.Application.Metrics;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticSdlc.Infrastructure.Metrics;

/// <summary>DI extension for the metrics layer.</summary>
public static class MetricsServiceCollectionExtensions
{
    /// <summary>Registers an <see cref="InMemoryMetricsCollector"/> singleton.</summary>
    public static IServiceCollection AddInMemoryMetrics(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<InMemoryMetricsCollector>();
        services.AddSingleton<IMetricsCollector>(sp => sp.GetRequiredService<InMemoryMetricsCollector>());
        return services;
    }

    /// <summary>Registers a <see cref="CsvMetricsCollector"/> singleton with the configured path.</summary>
    public static IServiceCollection AddCsvMetrics(this IServiceCollection services, string filePath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var collector = new CsvMetricsCollector(filePath);
        services.AddSingleton<CsvMetricsCollector>(collector);
        services.AddSingleton<IMetricsCollector>(collector);
        return services;
    }
}
