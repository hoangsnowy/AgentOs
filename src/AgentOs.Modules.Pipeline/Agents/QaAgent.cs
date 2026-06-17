// Phase 4 — IQaAgent impl. Assesses requirement-code-test consistency; drives the orchestrator's QA loop.
// The LLM-call / parse / metrics / error skeleton lives in LlmAgentBase. QA has no JSON schema, so it
// skips schema validation (no validator) and deserializes the raw content directly.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Modules.Pipeline.Metrics;
using AgentOs.Modules.Pipeline.Prompts;
using AgentOs.Domain;
using AgentOs.Domain.Code;
using AgentOs.Domain.Llm;
using AgentOs.Domain.Qa;
using AgentOs.Domain.Requirements;
using AgentOs.Domain.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentOs.Modules.Pipeline.Agents;

/// <summary>QA — assesses requirement-code-test consistency. Drives the orchestrator's QA loop.</summary>
public sealed class QaAgent : LlmAgentBase, IQaAgent
{
    /// <summary>Initializes.</summary>
    public QaAgent(
        ILlmClientFactory factory,
        IOptions<AgentsOptions> options,
        IMetricsCollector collector,
        ILogger<QaAgent> logger,
        IPromptOverrides? prompts = null)
        : base(factory, Slice(options), collector, logger, validator: null, prompts)
    {
    }

    private static AgentOptions Slice(IOptions<AgentsOptions> options)
    {
        System.ArgumentNullException.ThrowIfNull(options);
        return options.Value.Qa;
    }

    protected override string PromptKey => "Qa";

    protected override string DefaultSystemPrompt => QaPrompt.System;

    // QA emits a bare JSON object; deserialize the content as-is (no schema, no fence-extraction step).
    protected override string ExtractPayload(string content) => content;

    /// <inheritdoc />
    public Task<QaReport> RunAsync(
        RequirementSpec spec,
        CodeArtifact code,
        TestArtifact tests,
        CancellationToken cancellationToken = default)
    {
        System.ArgumentNullException.ThrowIfNull(spec);
        System.ArgumentNullException.ThrowIfNull(code);
        System.ArgumentNullException.ThrowIfNull(tests);
        return ExecuteAsync<QaReportDto, QaReport>(
            QaPrompt.RenderUser(spec, code, tests),
            dto => dto.Validate(AgentName),
            Map,
            LogSuccess,
            cancellationToken);
    }

    private static QaReport Map(QaReportDto dto, AgentMetrics metrics)
        => new(
            Score: dto.Score,
            IsConsistent: dto.IsConsistent,
            IterationNeeded: dto.IterationNeeded,
            // QA has no JSON schema (its output is advisory), so an issue may arrive with missing fields.
            // Coalesce to safe defaults rather than dereferencing null into the non-nullable QaIssue record —
            // one malformed issue must not poison the report or crash the orchestrator's QA loop.
            Issues: (dto.Issues ?? [])
                .Select(i => new QaIssue(
                    string.IsNullOrWhiteSpace(i.Severity) ? "info" : i.Severity,
                    string.IsNullOrWhiteSpace(i.Category) ? "general" : i.Category,
                    i.Description ?? string.Empty,
                    i.Location))
                .ToArray(),
            Recommendations: dto.Recommendations ?? [],
            Metrics: metrics);

    private void LogSuccess(AgentMetrics metrics, QaReportDto dto)
        => Logger.LogInformation(
            "{Agent} done: {InTok}→{OutTok} tokens, ${Cost} USD, {Ms}ms — score={Score} consistent={Consistent} issues={Issues}",
            AgentName, metrics.InputTokens, metrics.OutputTokens, metrics.CostUsd,
            metrics.Latency.TotalMilliseconds, dto.Score, dto.IsConsistent, dto.Issues?.Count ?? 0);

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
