// AgenticSdlc.Infrastructure/Agents/CodingAgent.cs
// Phase 4 — Impl ICodingAgent. Sinh source code C# Clean Architecture từ RequirementSpec.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Application.Agents;
using AgenticSdlc.Application.Metrics;
using AgenticSdlc.Application.Prompts;
using AgenticSdlc.Application.Validation;
using AgenticSdlc.Domain;
using AgenticSdlc.Domain.Code;
using AgenticSdlc.Domain.Llm;
using AgenticSdlc.Domain.Qa;
using AgenticSdlc.Domain.Requirements;
using AgenticSdlc.Infrastructure.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgenticSdlc.Infrastructure.Agents;

/// <summary>Sinh source code C# Clean Architecture từ requirement spec (+ optional QA feedback).</summary>
public sealed class CodingAgent : ICodingAgent
{
    private const string AgentName = nameof(CodingAgent);

    private readonly ILlmClient _llm;
    private readonly ILlmOutputValidator _validator;
    private readonly IMetricsCollector _collector;
    private readonly AgentOptions _options;
    private readonly ILogger<CodingAgent> _logger;

    /// <summary>Khởi tạo.</summary>
    public CodingAgent(
        ILlmClientFactory factory,
        IOptions<AgentsOptions> options,
        ILlmOutputValidator validator,
        IMetricsCollector collector,
        ILogger<CodingAgent> logger)
    {
        System.ArgumentNullException.ThrowIfNull(factory);
        System.ArgumentNullException.ThrowIfNull(options);
        _options = options.Value.Coding;
        _llm = factory.Create(_options.Provider);
        _validator = validator ?? throw new System.ArgumentNullException(nameof(validator));
        _collector = collector ?? throw new System.ArgumentNullException(nameof(collector));
        _logger = logger ?? throw new System.ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<CodeArtifact> RunAsync(
        RequirementSpec spec,
        QaReport? previousFeedback = null,
        CancellationToken cancellationToken = default)
    {
        System.ArgumentNullException.ThrowIfNull(spec);

        var request = new LlmRequest(
            SystemPrompt: CodingPrompt.System,
            UserPrompt: CodingPrompt.RenderUser(spec, previousFeedback),
            Model: _options.Model,
            Temperature: _options.Temperature,
            MaxTokens: _options.MaxTokens);

        var response = await _llm.SendAsync(request, cancellationToken).ConfigureAwait(false);

        try
        {
            var json = JsonExtractor.ExtractJson(response.Content, AgentName);
            _validator.Validate(json, SchemaNames.CodeArtifactV1, AgentName);

            var dto = JsonExtractor.Deserialize<CodeArtifactDto>(json, AgentName);
            dto.Validate(AgentName);

            var metrics = MetricsMapper.From(response);
            _collector.Add(RunMetricFactory.From(response, AgentName, success: true, errorMessage: null));

            _logger.LogInformation(
                "{Agent} done: {InTok}→{OutTok} tokens, ${Cost} USD, {Ms}ms — {FileCount} files",
                AgentName, metrics.InputTokens, metrics.OutputTokens, metrics.CostUsd,
                metrics.Latency.TotalMilliseconds, dto.Files!.Count);

            return new CodeArtifact(
                ProjectName: dto.ProjectName!,
                Architecture: dto.Architecture ?? "Clean Architecture",
                Files: dto.Files!.Select(f => new CodeFile(f.Path!, f.Content ?? string.Empty, f.Language ?? "csharp")).ToArray(),
                Notes: dto.Notes,
                Metrics: metrics);
        }
        catch (LlmException ex)
        {
            _collector.Add(RunMetricFactory.From(response, AgentName, success: false, errorMessage: ex.Message));
            throw;
        }
    }

    // ---- DTOs ----
    private sealed class CodeArtifactDto
    {
        public string? ProjectName { get; set; }
        public string? Architecture { get; set; }
        public List<FileDto>? Files { get; set; }
        public string? Notes { get; set; }

        public void Validate(string agentName)
        {
            if (string.IsNullOrWhiteSpace(ProjectName))
            {
                throw new LlmException($"{agentName}: missing 'projectName'.", agentName);
            }
            if (Files is null || Files.Count == 0)
            {
                throw new LlmException($"{agentName}: 'files' must have ≥ 1 item.", agentName);
            }
        }
    }

    private sealed class FileDto
    {
        public string? Path { get; set; }
        public string? Content { get; set; }
        public string? Language { get; set; }
    }
}
