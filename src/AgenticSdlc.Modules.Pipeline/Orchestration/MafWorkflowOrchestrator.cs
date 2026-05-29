// AgenticSdlc.Infrastructure/Orchestration/MafWorkflowOrchestrator.cs
// platform-v2 (feat/sdk-maf) — IOrchestratorAgent implemented as a Microsoft Agent Framework *Workflow* graph.
// Executors delegate to the existing typed specialist agents (which run on the SDK when Llm:ForceProvider=MAF),
// so all JSON parsing / validation / metrics are reused; MAF Workflows owns sequencing + the conditional QA loop.
// Selected via Pipeline:Engine=Workflow. NOTE: graph runtime semantics are verified with a live Azure key.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Modules.Pipeline.Agents;
using AgenticSdlc.Modules.Pipeline.Pipeline;
using AgenticSdlc.Domain;
using AgenticSdlc.Domain.Code;
using AgenticSdlc.Domain.Pipeline;
using AgenticSdlc.Domain.Qa;
using AgenticSdlc.Domain.Requirements;
using AgenticSdlc.Domain.Testing;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgenticSdlc.Modules.Pipeline.Orchestration;

/// <summary>
/// KC4 pipeline expressed as a MAF Workflow: Requirement → Coding → Testing → QA, with a conditional edge
/// QA → Coding while the QA verdict is not consistent and the iteration budget is not exhausted.
/// </summary>
public sealed class MafWorkflowOrchestrator : IOrchestratorAgent
{
    private readonly IRequirementAgent _requirement;
    private readonly ICodingAgent _coding;
    private readonly ITestingAgent _testing;
    private readonly IQaAgent _qa;
    private readonly PipelineOptions _options;
    private readonly ILogger<MafWorkflowOrchestrator> _logger;
    private readonly IPipelineProgressSink _progress;

    /// <summary>Initializes the workflow orchestrator (same dependencies as the classic orchestrator).</summary>
    public MafWorkflowOrchestrator(
        IRequirementAgent requirement,
        ICodingAgent coding,
        ITestingAgent testing,
        IQaAgent qa,
        IOptions<PipelineOptions> options,
        ILogger<MafWorkflowOrchestrator> logger,
        IPipelineProgressSink? progress = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _requirement = requirement ?? throw new ArgumentNullException(nameof(requirement));
        _coding = coding ?? throw new ArgumentNullException(nameof(coding));
        _testing = testing ?? throw new ArgumentNullException(nameof(testing));
        _qa = qa ?? throw new ArgumentNullException(nameof(qa));
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _progress = progress ?? NullPipelineProgressSink.Instance;
    }

    /// <inheritdoc />
    public async Task<PipelineResult> RunAsync(UserStory story, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(story);
        story.Validate();

        var maxIterations = Math.Min(story.NMax, _options.MaxIterations);
        var ctx = new PipelineState(story, maxIterations);

        var requirementEx = new RequirementExecutor(_requirement, _progress);
        var codingEx = new CodingExecutor(_coding, _progress);
        var testingEx = new TestingExecutor(_testing, _progress);
        var qaEx = new QaExecutor(_qa, _progress);

        var workflow = new WorkflowBuilder(requirementEx)
            .AddEdge(requirementEx, codingEx)
            .AddEdge(codingEx, testingEx)
            .AddEdge(testingEx, qaEx)
            .AddEdge(qaEx, codingEx, (PipelineState? s) => s is not null && !s.LastConsistent && s.Iteration < s.MaxIterations)
            .WithOutputFrom(qaEx)
            .Build();

        var run = await InProcessExecution.RunAsync(workflow, ctx, cancellationToken: cancellationToken).ConfigureAwait(false);

        PipelineState? final = null;
        foreach (var ev in run.NewEvents)
        {
            if (ev is WorkflowOutputEvent output && output.Data is PipelineState ps)
            {
                final = ps;
            }
        }

        return (final ?? ctx).ToResult();
    }

    // ----- mutable state threaded along every edge -----

    internal sealed class PipelineState(UserStory story, int maxIterations)
    {
        public UserStory Story { get; } = story;
        public int MaxIterations { get; } = maxIterations;
        public int Iteration { get; set; }
        public RequirementSpec? Spec { get; set; }
        public CodeArtifact? Code { get; set; }
        public TestArtifact? Tests { get; set; }
        public List<QaReport> QaHistory { get; } = new();
        public bool LastConsistent { get; set; }

        public PipelineResult ToResult()
        {
            var status = LastConsistent ? PipelineStatus.Done : PipelineStatus.MaxIterationReached;
            var total = Spec?.Metrics ?? AgentMetrics.Empty;
            if (Code is not null) { total = total.Add(Code.Metrics); }
            if (Tests is not null) { total = total.Add(Tests.Metrics); }
            foreach (var q in QaHistory) { total = total.Add(q.Metrics); }
            return new PipelineResult(Story, Spec!, Code!, Tests!, QaHistory, status, total);
        }
    }

    // ----- executors (delegate to the existing typed agents) -----

    private sealed class RequirementExecutor(IRequirementAgent agent, IPipelineProgressSink progress)
        : Executor<PipelineState, PipelineState>("requirement")
    {
        public override async ValueTask<PipelineState> HandleAsync(PipelineState s, IWorkflowContext context, CancellationToken ct)
        {
            await progress.ReportAsync(new PipelineProgressEvent(PipelineStage.Requirement, PipelinePhase.Started, 0, s.MaxIterations, "Analyze user story"), ct).ConfigureAwait(false);
            s.Spec = await agent.RunAsync(s.Story, ct).ConfigureAwait(false);
            await progress.ReportAsync(new PipelineProgressEvent(PipelineStage.Requirement, PipelinePhase.Completed, 0, s.MaxIterations, s.Spec.Title, Metrics: s.Spec.Metrics), ct).ConfigureAwait(false);
            return s;
        }
    }

    private sealed class CodingExecutor(ICodingAgent agent, IPipelineProgressSink progress)
        : Executor<PipelineState, PipelineState>("coding")
    {
        public override async ValueTask<PipelineState> HandleAsync(PipelineState s, IWorkflowContext context, CancellationToken ct)
        {
            s.Iteration++;
            var last = s.QaHistory.Count > 0 ? s.QaHistory[^1] : null;
            await progress.ReportAsync(new PipelineProgressEvent(PipelineStage.Coding, PipelinePhase.Started, s.Iteration, s.MaxIterations, last is null ? "Generate code" : "Regenerate from QA feedback"), ct).ConfigureAwait(false);
            s.Code = await agent.RunAsync(s.Spec!, last, ct).ConfigureAwait(false);
            await progress.ReportAsync(new PipelineProgressEvent(PipelineStage.Coding, PipelinePhase.Completed, s.Iteration, s.MaxIterations, $"{s.Code.Files.Count} files", Metrics: s.Code.Metrics), ct).ConfigureAwait(false);
            return s;
        }
    }

    private sealed class TestingExecutor(ITestingAgent agent, IPipelineProgressSink progress)
        : Executor<PipelineState, PipelineState>("testing")
    {
        public override async ValueTask<PipelineState> HandleAsync(PipelineState s, IWorkflowContext context, CancellationToken ct)
        {
            var last = s.QaHistory.Count > 0 ? s.QaHistory[^1] : null;
            await progress.ReportAsync(new PipelineProgressEvent(PipelineStage.Testing, PipelinePhase.Started, s.Iteration, s.MaxIterations, "Generate tests"), ct).ConfigureAwait(false);
            s.Tests = await agent.RunAsync(s.Spec!, s.Code!, last, ct).ConfigureAwait(false);
            await progress.ReportAsync(new PipelineProgressEvent(PipelineStage.Testing, PipelinePhase.Completed, s.Iteration, s.MaxIterations, $"{s.Tests.TotalCount} tests", Metrics: s.Tests.Metrics), ct).ConfigureAwait(false);
            return s;
        }
    }

    private sealed class QaExecutor(IQaAgent agent, IPipelineProgressSink progress)
        : Executor<PipelineState, PipelineState>("qa")
    {
        public override async ValueTask<PipelineState> HandleAsync(PipelineState s, IWorkflowContext context, CancellationToken ct)
        {
            await progress.ReportAsync(new PipelineProgressEvent(PipelineStage.Qa, PipelinePhase.Started, s.Iteration, s.MaxIterations, "Assess consistency"), ct).ConfigureAwait(false);
            var report = await agent.RunAsync(s.Spec!, s.Code!, s.Tests!, ct).ConfigureAwait(false);
            s.QaHistory.Add(report);
            s.LastConsistent = report.IsConsistent;
            await progress.ReportAsync(new PipelineProgressEvent(PipelineStage.Qa, PipelinePhase.Completed, s.Iteration, s.MaxIterations, report.IsConsistent ? "QA pass" : "QA not passing", QaScore: report.Score, QaConsistent: report.IsConsistent, Metrics: report.Metrics), ct).ConfigureAwait(false);
            return s;
        }
    }
}
