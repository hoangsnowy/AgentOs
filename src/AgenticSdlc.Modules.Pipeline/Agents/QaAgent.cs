// AgenticSdlc.Infrastructure/Agents/QaAgent.cs
// Phase 4 — IQaAgent impl. Assesses requirement-code-test consistency.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Modules.Pipeline.Agents;
using AgenticSdlc.Modules.Pipeline.Metrics;
using AgenticSdlc.Modules.Pipeline.Prompts;
using AgenticSdlc.Domain;
using AgenticSdlc.Domain.Code;
using AgenticSdlc.Domain.Llm;
using AgenticSdlc.Domain.Qa;
using AgenticSdlc.Domain.Requirements;
using AgenticSdlc.Domain.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgenticSdlc.Modules.Pipeline.Agents;

/// <summary>QA — assesses requirement-code-test consistency. Drives the orchestrator's QA loop.</summary>
public sealed class QaAgent : IQaAgent
{
    private const string AgentName = nameof(QaAgent);

    private readonly ILlmClient _llm;
    private readonly IMetricsCollector _collector;
    private readonly AgentOptions _options;
    private readonly ILogger<QaAgent> _logger;

    /// <summary>Initializes.</summary>
    public QaAgent(
        ILlmClientFactory factory,
        IOptions<AgentsOptions> options,
        IMetricsCollector collector,
        ILogger<QaAgent> logger)
    {
        System.ArgumentNullException.ThrowIfNull(factory);
        System.ArgumentNullException.ThrowIfNull(options);
        _options = options.Value.Qa;
        _llm = factory.Create(_options.Provider);
        _collector = collector ?? throw new System.ArgumentNullException(nameof(collector));
        _logger = logger ?? throw new System.ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<QaReport> RunAsync(
        RequirementSpec spec,
        CodeArtifact code,
        TestArtifact tests,
        CancellationToken cancellationToken = default)
    {
        System.ArgumentNullException.ThrowIfNull(spec);
        System.ArgumentNullException.ThrowIfNull(code);
        System.ArgumentNullException.ThrowIfNull(tests);

        var request = new LlmRequest(
            SystemPrompt: QaPrompt.System,
            UserPrompt: QaPrompt.RenderUser(spec, code, tests),
            Model: _options.Model,
            Temperature: _options.Temperature,
            MaxTokens: _options.MaxTokens);

        var response = await _llm.SendAsync(request, cancellationToken).ConfigureAwait(false);

        try
        {
            var dto = JsonExtractor.Deserialize<QaReportDto>(response.Content, AgentName);
            dto.Validate(AgentName);

            var metrics = MetricsMapper.From(response);
            _collector.Add(RunMetricFactory.From(response, AgentName, success: true, errorMessage: null));

            _logger.LogInformation(
                "{Agent} done: {InTok}→{OutTok} tokens, ${Cost} USD, {Ms}ms — score={Score} consistent={Consistent} issues={Issues}",
                AgentName, metrics.InputTokens, metrics.OutputTokens, metrics.CostUsd,
                metrics.Latency.TotalMilliseconds, dto.Score, dto.IsConsistent, dto.Issues?.Count ?? 0);

            return new QaReport(
                Score: dto.Score,
                IsConsistent: dto.IsConsistent,
                IterationNeeded: dto.IterationNeeded,
                Issues: (dto.Issues ?? []).Select(i => new QaIssue(i.Severity!, i.Category!, i.Description!, i.Location)).ToArray(),
                Recommendations: dto.Recommendations ?? [],
                Metrics: metrics);
        }
        catch (LlmException ex)
        {
            _collector.Add(RunMetricFactory.From(response, AgentName, success: false, errorMessage: ex.Message));
            throw;
        }
    }

    // ---- DTOs ----
    private sealed class QaReportDto
    {
        public double Score { get; set; }
        public bool IsConsistent { get; set; }
        public bool IterationNeeded { get; set; }
        public List<QaIssueDto>? Issues { get; set; }
        public List<string>? Recommendations { get; set; }

        public void Validate(string agentName)
        {
            if (Score is < 0.0 or > 1.0)
            {
                throw new LlmException($"{agentName}: 'score' must be in [0, 1] (got {Score}).", agentName);
            }
        }
    }

    private sealed class QaIssueDto
    {
        public string? Severity { get; set; }
        public string? Category { get; set; }
        public string? Description { get; set; }
        public string? Location { get; set; }
    }
}
