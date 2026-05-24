// AgenticSdlc.Tests/EndToEnd/KcBenchHarness.cs
// Sprint 4 — shared harness for the 5 KC test classes. Single CSV sink TestResults/kc_metrics.csv.

using System;
using System.IO;
using AgenticSdlc.Application.Agents;
using AgenticSdlc.Application.Metrics;
using AgenticSdlc.Application.Pipeline;
using AgenticSdlc.Application.Validation;
using AgenticSdlc.Domain.Llm;
using AgenticSdlc.Infrastructure.Agents;
using AgenticSdlc.Infrastructure.Llm;
using AgenticSdlc.Infrastructure.Metrics;
using AgenticSdlc.Infrastructure.Orchestration;
using AgenticSdlc.Infrastructure.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AgenticSdlc.Tests.EndToEnd;

/// <summary>Shared bench infra — a single CSV collector for all KC test classes.</summary>
public static class KcBenchHarness
{
    /// <summary>Path to the CSV output, relative to the test run dir.</summary>
    public static readonly string CsvPath =
        Path.Combine(AppContext.BaseDirectory, "TestResults", "kc_metrics.csv");

    private static readonly Lazy<CsvMetricsCollector> _collector = new(() =>
    {
        // Wipe the file at the start of the first test run.
        if (File.Exists(CsvPath))
        {
            File.Delete(CsvPath);
        }
        var dir = Path.GetDirectoryName(CsvPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        return new CsvMetricsCollector(CsvPath);
    });

    /// <summary>Shared CSV collector.</summary>
    public static CsvMetricsCollector Collector => _collector.Value;

    private static readonly Lazy<ILlmOutputValidator> _validator = new(() =>
    {
        var sc = new ServiceCollection();
        sc.AddValidation();
        return sc.BuildServiceProvider().GetRequiredService<ILlmOutputValidator>();
    });

    /// <summary>Shared validator (3 schemas embedded).</summary>
    public static ILlmOutputValidator Validator => _validator.Value;

    /// <summary>Build a single agent (Requirement / Coding / Testing / Qa) wired to the shared collector.</summary>
    public static RequirementAgent BuildRequirement(ILlmClient llm)
    {
        var factory = FactoryReturning(llm);
        return new RequirementAgent(factory, Options.Create(new AgentsOptions()), Validator, Collector, NullLogger<RequirementAgent>.Instance);
    }

    public static CodingAgent BuildCoding(ILlmClient llm)
    {
        var factory = FactoryReturning(llm);
        return new CodingAgent(factory, Options.Create(new AgentsOptions()), Validator, Collector, NullLogger<CodingAgent>.Instance);
    }

    public static TestingAgent BuildTesting(ILlmClient llm)
    {
        var factory = FactoryReturning(llm);
        return new TestingAgent(factory, Options.Create(new AgentsOptions()), Validator, Collector, NullLogger<TestingAgent>.Instance);
    }

    public static QaAgent BuildQa(ILlmClient llm)
    {
        var factory = FactoryReturning(llm);
        return new QaAgent(factory, Options.Create(new AgentsOptions()), Collector, NullLogger<QaAgent>.Instance);
    }

    public static PipelineOrchestrator BuildOrchestrator(ILlmClient llm)
    {
        return new PipelineOrchestrator(
            BuildRequirement(llm),
            BuildCoding(llm),
            BuildTesting(llm),
            BuildQa(llm),
            Options.Create(new PipelineOptions { MaxIterations = 3 }),
            NullLogger<PipelineOrchestrator>.Instance);
    }

    private static ILlmClientFactory FactoryReturning(ILlmClient client)
    {
        var factory = Substitute.For<ILlmClientFactory>();
        factory.Create(Arg.Any<string>()).Returns(client);
        return factory;
    }
}
