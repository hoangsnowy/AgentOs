// AgentOs.Infrastructure/Agents/CodingAgent.cs
// Phase 4 — ICodingAgent impl. Generates C# Clean Architecture source code from a RequirementSpec.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Modules.Pipeline.Agents;
using AgentOs.Modules.Pipeline.Metrics;
using AgentOs.Modules.Pipeline.Prompts;
using AgentOs.Modules.Pipeline.Validation;
using AgentOs.Domain;
using AgentOs.Domain.Code;
using AgentOs.Domain.Llm;
using AgentOs.Domain.Qa;
using AgentOs.Domain.Requirements;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentOs.Modules.Pipeline.Agents;

/// <summary>Generates C# Clean Architecture source code from a requirement spec (+ optional QA feedback).</summary>
public sealed class CodingAgent : ICodingAgent
{
    private const string AgentName = nameof(CodingAgent);

    private readonly ILlmClient _llm;
    private readonly ILlmOutputValidator _validator;
    private readonly IMetricsCollector _collector;
    private readonly AgentOptions _options;
    private readonly ILogger<CodingAgent> _logger;
    private readonly IPromptOverrides? _prompts;

    /// <summary>Initializes.</summary>
    public CodingAgent(
        ILlmClientFactory factory,
        IOptions<AgentsOptions> options,
        ILlmOutputValidator validator,
        IMetricsCollector collector,
        ILogger<CodingAgent> logger,
        IPromptOverrides? prompts = null)
    {
        System.ArgumentNullException.ThrowIfNull(factory);
        System.ArgumentNullException.ThrowIfNull(options);
        _options = options.Value.Coding;
        _llm = factory.Create(_options.Provider);
        _validator = validator ?? throw new System.ArgumentNullException(nameof(validator));
        _collector = collector ?? throw new System.ArgumentNullException(nameof(collector));
        _logger = logger ?? throw new System.ArgumentNullException(nameof(logger));
        _prompts = prompts;
    }

    /// <inheritdoc />
    public async Task<CodeArtifact> RunAsync(
        RequirementSpec spec,
        QaReport? previousFeedback = null,
        CancellationToken cancellationToken = default)
    {
        System.ArgumentNullException.ThrowIfNull(spec);

        var systemPrompt = _prompts is null
            ? CodingPrompt.System
            : await _prompts.ResolveAsync("Coding", CodingPrompt.System, cancellationToken).ConfigureAwait(false);

        var request = new LlmRequest(
            SystemPrompt: systemPrompt,
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
