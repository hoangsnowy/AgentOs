// Coherence Phase 2 (A2a) — the Spine feed line for a Quality run's pipeline progress.

using AgentOs.Domain.Pipeline;
using AgentOs.Modules.Pipeline.Sessions;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Pipeline;

public sealed class PipelineProgressFormatterTests
{
    [Fact]
    public void Describe_WithMessage_ShowsIterationStageMessage()
    {
        var e = new PipelineProgressEvent(PipelineStage.Coding, PipelinePhase.Started, 2, 3, "Generating code");
        PipelineProgressFormatter.Describe(e).ShouldBe("Iter 2 · Coding: Generating code");
    }

    [Fact]
    public void Describe_NoMessage_FallsBackToPhase()
    {
        var e = new PipelineProgressEvent(PipelineStage.Qa, PipelinePhase.Completed, 1, 3, "");
        PipelineProgressFormatter.Describe(e).ShouldBe("Iter 1 · Qa: Completed");
    }
}
