// AgenticSdlc.Infrastructure/Metrics/DependencyInjection.cs
// Sprint 4 — DI cho IMetricsCollector.

using System;
using AgenticSdlc.Application.Metrics;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticSdlc.Infrastructure.Metrics;

/// <summary>DI extension cho metrics layer.</summary>
public static class MetricsServiceCollectionExtensions
{
    /// <summary>Đăng ký <see cref="InMemoryMetricsCollector"/> singleton.</summary>
    public static IServiceCollection AddInMemoryMetrics(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<InMemoryMetricsCollector>();
        services.AddSingleton<IMetricsCollector>(sp => sp.GetRequiredService<InMemoryMetricsCollector>());
        return services;
    }

    /// <summary>Đăng ký <see cref="CsvMetricsCollector"/> singleton với path cấu hình.</summary>
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
