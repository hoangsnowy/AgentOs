// AgentOs.Infrastructure/Agents/RequirementAgent.cs
// Phase 4 — IRequirementAgent impl. Calls the LLM with a structured-output system prompt and parses JSON.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Modules.Pipeline.Agents;
using AgentOs.Modules.Pipeline.Metrics;
using AgentOs.Modules.Pipeline.Prompts;
using AgentOs.Modules.Pipeline.Validation;
using AgentOs.Domain;
using AgentOs.Domain.Llm;
using AgentOs.Domain.Pipeline;
using AgentOs.Domain.Requirements;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentOs.Modules.Pipeline.Agents;

/// <summary>Analyzes a user story → a structured JSON <see cref="RequirementSpec"/>.</summary>
public sealed class RequirementAgent : IRequirementAgent
{
    private const string AgentName = nameof(RequirementAgent);

    private readonly ILlmClient _llm;
    private readonly ILlmOutputValidator _validator;
    private readonly IMetricsCollector _collector;
    private readonly AgentOptions _options;
    private readonly ILogger<RequirementAgent> _logger;
    private readonly IPromptOverrides? _prompts;

    /// <summary>Initializes.</summary>
    public RequirementAgent(
        ILlmClientFactory factory,
        IOptions<AgentsOptions> options,
        ILlmOutputValidator validator,
        IMetricsCollector collector,
        ILogger<RequirementAgent> logger,
        IPromptOverrides? prompts = null)
    {
        System.ArgumentNullException.ThrowIfNull(factory);
        System.ArgumentNullException.ThrowIfNull(options);
        _options = options.Value.Requirement;
        _llm = factory.Create(_options.Provider);
        _validator = validator ?? throw new System.ArgumentNullException(nameof(validator));
        _collector = collector ?? throw new System.ArgumentNullException(nameof(collector));
        _logger = logger ?? throw new System.ArgumentNullException(nameof(logger));
        _prompts = prompts;
    }

    /// <inheritdoc />
    public async Task<RequirementSpec> RunAsync(UserStory story, CancellationToken cancellationToken = default)
    {
        System.ArgumentNullException.ThrowIfNull(story);
        story.Validate();

        var systemPrompt = _prompts is null
            ? RequirementPrompt.System
            : await _prompts.ResolveAsync("Requirement", RequirementPrompt.System, cancellationToken).ConfigureAwait(false);

        var request = new LlmRequest(
            SystemPrompt: systemPrompt,
            UserPrompt: RequirementPrompt.RenderUser(story),
            Model: _options.Model,
            Temperature: _options.Temperature,
            MaxTokens: _options.MaxTokens);

        var response = await _llm.SendAsync(request, cancellationToken).ConfigureAwait(false);

        try
        {
            var json = JsonExtractor.ExtractJson(response.Content, AgentName);
            _validator.Validate(json, SchemaNames.RequirementSpecV1, AgentName);

            var dto = JsonExtractor.Deserialize<RequirementSpecDto>(json, AgentName);
            dto.Validate(AgentName);

            var metrics = MetricsMapper.From(response);
            _collector.Add(RunMetricFactory.From(response, AgentName, success: true, errorMessage: null));

            _logger.LogInformation(
                "{Agent} done: {InTok}→{OutTok} tokens, ${Cost} USD, {Ms}ms ({Provider} {Model})",
                AgentName, metrics.InputTokens, metrics.OutputTokens, metrics.CostUsd,
                metrics.Latency.TotalMilliseconds, metrics.Provider, metrics.Model);

            return Map(dto, metrics);
        }
        catch (LlmException ex)
        {
            _collector.Add(RunMetricFactory.From(response, AgentName, success: false, errorMessage: ex.Message));
            throw;
        }
    }

    private static RequirementSpec Map(RequirementSpecDto dto, AgentMetrics metrics)
        => new(
            Title: dto.Title!,
            Summary: dto.Summary!,
            Stakeholders: dto.Stakeholders ?? [],
            FunctionalRequirements: dto.FunctionalRequirements ?? [],
            NonFunctionalRequirements: dto.NonFunctionalRequirements ?? [],
            Entities: (dto.Entities ?? []).Select(e => new EntityDescriptor(e.Name!, e.Fields ?? [], e.Notes)).ToArray(),
            Endpoints: (dto.Endpoints ?? []).Select(e => new EndpointDescriptor(e.Method!, e.Path!, e.Purpose!, e.AuthRequired)).ToArray(),
            AcceptanceCriteria: dto.AcceptanceCriteria ?? [],
            Metrics: metrics);

    // ---- DTOs for JSON deserialization (matching the schema in the SystemPrompt) ----
    private sealed class RequirementSpecDto
    {
        public string? Title { get; set; }
        public string? Summary { get; set; }
        public List<string>? Stakeholders { get; set; }
        public List<string>? FunctionalRequirements { get; set; }
        public List<string>? NonFunctionalRequirements { get; set; }
        public List<EntityDto>? Entities { get; set; }
        public List<EndpointDto>? Endpoints { get; set; }
        public List<string>? AcceptanceCriteria { get; set; }

        public void Validate(string agentName)
        {
            if (string.IsNullOrWhiteSpace(Title))
            {
                throw new LlmException($"{agentName}: missing 'title'.", agentName);
            }
            if (string.IsNullOrWhiteSpace(Summary))
            {
                throw new LlmException($"{agentName}: missing 'summary'.", agentName);
            }
            if (Entities is null || Entities.Count == 0)
            {
                throw new LlmException($"{agentName}: 'entities' must have ≥ 1 item.", agentName);
            }
            if (Endpoints is null || Endpoints.Count == 0)
            {
                throw new LlmException($"{agentName}: 'endpoints' must have ≥ 1 item.", agentName);
            }
        }
    }

    private sealed class EntityDto
    {
        public string? Name { get; set; }
        public List<string>? Fields { get; set; }
        public string? Notes { get; set; }
    }

    private sealed class EndpointDto
    {
        public string? Method { get; set; }
        public string? Path { get; set; }
        public string? Purpose { get; set; }
        public bool AuthRequired { get; set; }
    }
}
