// AgenticSdlc.Tests/Agents/AgentTestHelpers.cs
// Phase 4 — Helpers to build an LlmResponse stub + AgentsOptions with a single agent.

using System;
using AgenticSdlc.Application.Agents;
using AgenticSdlc.Application.Metrics;
using AgenticSdlc.Application.Validation;
using AgenticSdlc.Domain.Llm;
using AgenticSdlc.Infrastructure.Llm;
using AgenticSdlc.Infrastructure.Metrics;
using AgenticSdlc.Infrastructure.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AgenticSdlc.Tests.Agents;

internal static class AgentTestHelpers
{
    private static readonly Lazy<ILlmOutputValidator> _validator = new(() =>
    {
        var sc = new ServiceCollection();
        sc.AddValidation();
        return sc.BuildServiceProvider().GetRequiredService<ILlmOutputValidator>();
    });

    /// <summary>Shared validator (3 schemas embedded).</summary>
    public static ILlmOutputValidator Validator => _validator.Value;

    /// <summary>New in-memory metrics collector (call per test to avoid state leak).</summary>
    public static InMemoryMetricsCollector NewCollector() => new();

    public static LlmResponse StubResponse(string content)
        => new(
            Content: content,
            InputTokens: 100,
            OutputTokens: 50,
            CostUsd: 0.0001m,
            Latency: TimeSpan.FromMilliseconds(123),
            Model: "mock-model",
            Provider: "Mock");

    public static IOptions<AgentsOptions> OptionsWith(AgentsOptions opts) => Options.Create(opts);

    public static ILlmClientFactory FactoryReturning(ILlmClient client)
    {
        var factory = Substitute.For<ILlmClientFactory>();
        factory.Create(Arg.Any<string>()).Returns(client);
        return factory;
    }
}
