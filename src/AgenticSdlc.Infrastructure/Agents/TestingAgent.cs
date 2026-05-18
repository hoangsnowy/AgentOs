// AgenticSdlc.Infrastructure/Agents/TestingAgent.cs
// Phase 4 — Impl ITestingAgent. Sinh xUnit test (happy/edge/error) từ spec + code.

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
using AgenticSdlc.Domain.Testing;
using AgenticSdlc.Infrastructure.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgenticSdlc.Infrastructure.Agents;

/// <summary>Sinh bộ test xUnit phân loại happy/edge/error.</summary>
public sealed class TestingAgent : ITestingAgent
{
    private const string AgentName = nameof(TestingAgent);

    private readonly ILlmClient _llm;
    private readonly ILlmOutputValidator _validator;
    private readonly IMetricsCollector _collector;
    private readonly AgentOptions _options;
    private readonly ILogger<TestingAgent> _logger;

    /// <summary>Khởi tạo.</summary>
    public TestingAgent(
        ILlmClientFactory factory,
        IOptions<AgentsOptions> options,
        ILlmOutputValidator validator,
        IMetricsCollector collector,
        ILogger<TestingAgent> logger)
    {
        System.ArgumentNullException.ThrowIfNull(factory);
        System.ArgumentNullException.ThrowIfNull(options);
        _options = options.Value.Testing;
        _llm = factory.Create(_options.Provider);
        _validator = validator ?? throw new System.ArgumentNullException(nameof(validator));
        _collector = collector ?? throw new System.ArgumentNullException(nameof(collector));
        _logger = logger ?? throw new System.ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<TestArtifact> RunAsync(
        RequirementSpec spec,
        CodeArtifact code,
        QaReport? previousFeedback = null,
        CancellationToken cancellationToken = default)
    {
        System.ArgumentNullException.ThrowIfNull(spec);
        System.ArgumentNullException.ThrowIfNull(code);

        var request = new LlmRequest(
            SystemPrompt: TestingPrompt.System,
            UserPrompt: TestingPrompt.RenderUser(spec, code, previousFeedback),
            Model: _options.Model,
            Temperature: _options.Temperature,
            MaxTokens: _options.MaxTokens);

        var response = await _llm.SendAsync(request, cancellationToken).ConfigureAwait(false);

        try
        {
            var json = JsonExtractor.ExtractJson(response.Content, AgentName);
            _validator.Validate(json, SchemaNames.TestArtifactV1, AgentName);

            var dto = JsonExtractor.Deserialize<TestArtifactDto>(json, AgentName);
            dto.Validate(AgentName);

            var metrics = MetricsMapper.From(response);
            _collector.Add(RunMetricFactory.From(response, AgentName, success: true, errorMessage: null));

            _logger.LogInformation(
                "{Agent} done: {InTok}→{OutTok} tokens, ${Cost} USD, {Ms}ms — {Total} tests ({Happy}H/{Edge}E/{Err}X)",
                AgentName, metrics.InputTokens, metrics.OutputTokens, metrics.CostUsd,
                metrics.Latency.TotalMilliseconds, dto.HappyPathCount + dto.EdgeCaseCount + dto.ErrorCaseCount,
                dto.HappyPathCount, dto.EdgeCaseCount, dto.ErrorCaseCount);

            return new TestArtifact(
                Framework: dto.Framework ?? "xUnit",
                Files: dto.Files!.Select(f => new CodeFile(f.Path!, f.Content ?? string.Empty, f.Language ?? "csharp")).ToArray(),
                HappyPathCount: dto.HappyPathCount,
                EdgeCaseCount: dto.EdgeCaseCount,
                ErrorCaseCount: dto.ErrorCaseCount,
                EstimatedCoveragePercent: dto.EstimatedCoveragePercent,
                Metrics: metrics);
        }
        catch (LlmException ex)
        {
            _collector.Add(RunMetricFactory.From(response, AgentName, success: false, errorMessage: ex.Message));
            throw;
        }
    }

    // ---- DTOs ----
    private sealed class TestArtifactDto
    {
        public string? Framework { get; set; }
        public List<FileDto>? Files { get; set; }
        public int HappyPathCount { get; set; }
        public int EdgeCaseCount { get; set; }
        public int ErrorCaseCount { get; set; }
        public int EstimatedCoveragePercent { get; set; }

        public void Validate(string agentName)
        {
            if (Files is null || Files.Count == 0)
            {
                throw new LlmException($"{agentName}: 'files' must have ≥ 1 item.", agentName);
            }
            if (HappyPathCount + EdgeCaseCount + ErrorCaseCount <= 0)
            {
                throw new LlmException($"{agentName}: total test count must be > 0.", agentName);
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
