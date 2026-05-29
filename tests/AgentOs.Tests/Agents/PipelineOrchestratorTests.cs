// AgentOs.Tests/Agents/PipelineOrchestratorTests.cs
// Phase 4 — Unit tests for PipelineOrchestrator (4 mocked agents).

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Modules.Pipeline.Agents;
using AgentOs.Modules.Pipeline.Pipeline;
using AgentOs.Domain;
using AgentOs.Domain.Code;
using AgentOs.Domain.Llm;
using AgentOs.Domain.Pipeline;
using AgentOs.Domain.Qa;
using AgentOs.Domain.Requirements;
using AgentOs.Domain.Testing;
using AgentOs.Modules.Pipeline.Orchestration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Agents;

public class PipelineOrchestratorTests
{
    [Fact]
    public async Task RunAsync_QaPassFirstIteration_ReturnsDone()
    {
        var spec = StubSpec();
        var code = StubCode();
        var tests = StubTests();
        var qa = StubQa(isConsistent: true);

        var orchestrator = Build(spec, code, tests, qa);
        var result = await orchestrator.RunAsync(new UserStory("story", NMax: 3));

        result.Status.ShouldBe(PipelineStatus.Done);
        result.IterationCount.ShouldBe(1);
        result.TotalMetrics.InputTokens.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task RunAsync_QaFailsAllIterations_ReturnsMaxIterationReached()
    {
        var spec = StubSpec();
        var code = StubCode();
        var tests = StubTests();
        var qa = StubQa(isConsistent: false);

        var orchestrator = Build(spec, code, tests, qa);
        var result = await orchestrator.RunAsync(new UserStory("story", NMax: 2));

        result.Status.ShouldBe(PipelineStatus.MaxIterationReached);
        result.IterationCount.ShouldBe(2);
    }

    [Fact]
    public async Task RunAsync_RequirementThrows_ReturnsFailedEarly()
    {
        var req = Substitute.For<IRequirementAgent>();
        req.RunAsync(Arg.Any<UserStory>(), Arg.Any<CancellationToken>())
           .ThrowsAsyncForAnyArgs(new LlmException("requirement boom"));

        var orchestrator = new PipelineOrchestrator(
            req,
            Substitute.For<ICodingAgent>(),
            Substitute.For<ITestingAgent>(),
            Substitute.For<IQaAgent>(),
            Options.Create(new PipelineOptions { MaxIterations = 3 }),
            NullLogger<PipelineOrchestrator>.Instance);

        var result = await orchestrator.RunAsync(new UserStory("story"));

        result.Status.ShouldBe(PipelineStatus.Failed);
        result.IterationCount.ShouldBe(0);
        result.Spec.Title.ShouldBe("(failed)");
    }

    [Fact]
    public async Task RunAsync_CodingThrows_ReturnsFailedMidway()
    {
        var req = Substitute.For<IRequirementAgent>();
        req.RunAsync(Arg.Any<UserStory>(), Arg.Any<CancellationToken>()).Returns(StubSpec());

        var coding = Substitute.For<ICodingAgent>();
        coding.RunAsync(Arg.Any<RequirementSpec>(), Arg.Any<QaReport?>(), Arg.Any<CancellationToken>())
              .ThrowsAsyncForAnyArgs(new LlmException("coding boom"));

        var orchestrator = new PipelineOrchestrator(
            req, coding,
            Substitute.For<ITestingAgent>(),
            Substitute.For<IQaAgent>(),
            Options.Create(new PipelineOptions { MaxIterations = 3 }),
            NullLogger<PipelineOrchestrator>.Instance);

        var result = await orchestrator.RunAsync(new UserStory("story"));

        result.Status.ShouldBe(PipelineStatus.Failed);
        result.Spec.Title.ShouldNotBe("(failed)"); // requirement succeeded
    }

    [Fact]
    public async Task RunAsync_StoryNMaxClampedToOptionsMax()
    {
        var spec = StubSpec();
        var code = StubCode();
        var tests = StubTests();
        var qa = StubQa(isConsistent: false);

        // Options cap = 2, story.NMax = 5 → cap wins.
        var orchestrator = Build(spec, code, tests, qa, optionsMax: 2);
        var result = await orchestrator.RunAsync(new UserStory("story", NMax: 5));

        result.IterationCount.ShouldBe(2);
    }

    private static PipelineOrchestrator Build(
        RequirementSpec spec, CodeArtifact code, TestArtifact tests, QaReport qa, int optionsMax = 5)
    {
        var req = Substitute.For<IRequirementAgent>();
        req.RunAsync(Arg.Any<UserStory>(), Arg.Any<CancellationToken>()).Returns(spec);

        var coding = Substitute.For<ICodingAgent>();
        coding.RunAsync(Arg.Any<RequirementSpec>(), Arg.Any<QaReport?>(), Arg.Any<CancellationToken>()).Returns(code);

        var testing = Substitute.For<ITestingAgent>();
        testing.RunAsync(Arg.Any<RequirementSpec>(), Arg.Any<CodeArtifact>(), Arg.Any<QaReport?>(), Arg.Any<CancellationToken>())
               .Returns(tests);

        var qaAgent = Substitute.For<IQaAgent>();
        qaAgent.RunAsync(Arg.Any<RequirementSpec>(), Arg.Any<CodeArtifact>(), Arg.Any<TestArtifact>(), Arg.Any<CancellationToken>())
               .Returns(qa);

        return new PipelineOrchestrator(
            req, coding, testing, qaAgent,
            Options.Create(new PipelineOptions { MaxIterations = optionsMax }),
            NullLogger<PipelineOrchestrator>.Instance);
    }

    private static RequirementSpec StubSpec() => new(
        Title: "T", Summary: "S",
        Stakeholders: [], FunctionalRequirements: [], NonFunctionalRequirements: [],
        Entities: [new EntityDescriptor("E", [])],
        Endpoints: [new EndpointDescriptor("GET", "/", "root")],
        AcceptanceCriteria: ["a"],
        Metrics: new AgentMetrics("Test", "m", 10, 5, 0.0001m, System.TimeSpan.FromMilliseconds(50)));

    private static CodeArtifact StubCode() => new(
        ProjectName: "P", Architecture: "Clean Architecture",
        Files: [new CodeFile("src/E.cs", "namespace P;")],
        Notes: null,
        Metrics: new AgentMetrics("Test", "m", 20, 10, 0.0002m, System.TimeSpan.FromMilliseconds(80)));

    private static TestArtifact StubTests() => new(
        Framework: "xUnit",
        Files: [new CodeFile("tests/ETests.cs", "namespace T;")],
        HappyPathCount: 1, EdgeCaseCount: 1, ErrorCaseCount: 1, EstimatedCoveragePercent: 60,
        Metrics: new AgentMetrics("Test", "m", 15, 8, 0.00015m, System.TimeSpan.FromMilliseconds(70)));

    private static QaReport StubQa(bool isConsistent) => new(
        Score: isConsistent ? 0.9 : 0.6,
        IsConsistent: isConsistent,
        IterationNeeded: !isConsistent,
        Issues: isConsistent ? [] : [new QaIssue("Major", "TestCoverage", "missing edge")],
        Recommendations: isConsistent ? [] : ["add edge test"],
        Metrics: new AgentMetrics("Test", "m", 12, 6, 0.0001m, System.TimeSpan.FromMilliseconds(40)));
}
