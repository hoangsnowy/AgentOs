// M5 / multi-repo — unit tests for IssueWorkAgent. Uses a mock ILlmClient so no real LLM calls run.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Llm;
using AgentOs.Domain.Sessions;
using AgentOs.Modules.Pipeline.Agents;
using AgentOs.Tests.Agents;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Sessions;

public class IssueWorkAgentTests
{
    private static IssueWorkRequest MakeRequest(int issueNumber = 42) => new(
        SessionId: Guid.NewGuid(),
        TenantId: "test-tenant",
        MemberId: "user-1",
        Repos: [new WorkRepo(Guid.NewGuid(), "acme", "my-service", "main")],
        IssueNumber: issueNumber,
        IssueTitle: $"Bug #{issueNumber}: Something is broken",
        IssueBody: "When X happens, Y fails.");

    private static IssueWorkRequest MakeMultiRepoRequest(int issueNumber, params string[] repos) => new(
        SessionId: Guid.NewGuid(),
        TenantId: "test-tenant",
        MemberId: "user-1",
        Repos: repos.Select(r => new WorkRepo(Guid.NewGuid(), "acme", r, "main")).ToList(),
        IssueNumber: issueNumber,
        IssueTitle: "Cross-service ticket",
        IssueBody: "Touches several services.");

    private static IssueWorkAgent MakeAgent(ILlmClient llm) =>
        new(AgentTestHelpers.FactoryReturning(llm),
            AgentTestHelpers.OptionsWith(new AgentsOptions()),
            NullLogger<IssueWorkAgent>.Instance);

    // ── Success path ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_SuccessJson_ReturnsBranchAndSummaryPerRepo()
    {
        var llm = Substitute.For<ILlmClient>();
        llm.SendAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
           .Returns(AgentTestHelpers.StubResponse(
               """{"branch":"issue-42-ai-fix","summary":"Replaced null check with guard clause."}"""));

        var result = await MakeAgent(llm).RunAsync(MakeRequest());

        result.Ok.ShouldBeTrue();
        result.Error.ShouldBeNull();
        var repo = result.Repos.ShouldHaveSingleItem();
        repo.Ok.ShouldBeTrue();
        repo.BranchName.ShouldBe("issue-42-ai-fix");
        repo.Summary.ShouldBe("Replaced null check with guard clause.");
    }

    [Fact]
    public async Task RunAsync_JsonEmbeddedInText_ExtractsLastJsonBlock()
    {
        var response = "I explored the repo and found the issue.\n" +
                       "{\"branch\":\"issue-42-ai-fix\",\"summary\":\"Added missing null check.\"}";

        var llm = Substitute.For<ILlmClient>();
        llm.SendAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
           .Returns(AgentTestHelpers.StubResponse(response));

        var result = await MakeAgent(llm).RunAsync(MakeRequest());

        result.Ok.ShouldBeTrue();
        result.Repos[0].BranchName.ShouldBe("issue-42-ai-fix");
    }

    // ── Multi-repo ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_MultipleRepos_RunsEachAndAggregatesOk()
    {
        var llm = Substitute.For<ILlmClient>();
        llm.SendAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
           .Returns(AgentTestHelpers.StubResponse(
               """{"branch":"issue-7-ai-fix","summary":"Done."}"""));

        var result = await MakeAgent(llm).RunAsync(MakeMultiRepoRequest(7, "api", "web", "worker"));

        result.Ok.ShouldBeTrue();
        result.Repos.Count.ShouldBe(3);
        result.Repos.ShouldAllBe(r => r.Ok);
        await llm.Received(3).SendAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_OneRepoFails_AggregateFails_OthersStillReported()
    {
        var llm = Substitute.For<ILlmClient>();
        llm.SendAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
           .Returns(
               AgentTestHelpers.StubResponse("""{"branch":"issue-9-ai-fix","summary":"Fixed api."}"""),
               AgentTestHelpers.StubResponse("""{"branch":"","summary":"","error":"Build failed in web."}"""));

        var result = await MakeAgent(llm).RunAsync(MakeMultiRepoRequest(9, "api", "web"));

        result.Ok.ShouldBeFalse();
        result.Repos.Count.ShouldBe(2);
        result.Repos[0].Ok.ShouldBeTrue();
        result.Repos[1].Ok.ShouldBeFalse();
        result.Repos[1].Error!.ShouldContain("Build failed");
    }

    [Fact]
    public async Task RunAsync_NoRepos_ReturnsFailure()
    {
        var llm = Substitute.For<ILlmClient>();
        var request = new IssueWorkRequest(
            Guid.NewGuid(), "t", "u", Array.Empty<WorkRepo>(), 1, "T", "B");

        var result = await MakeAgent(llm).RunAsync(request);

        result.Ok.ShouldBeFalse();
        result.Repos.ShouldBeEmpty();
        await llm.DidNotReceive().SendAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>());
    }

    // ── Failure paths ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_AgentReportsError_ReturnsFailure()
    {
        var llm = Substitute.For<ILlmClient>();
        llm.SendAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
           .Returns(AgentTestHelpers.StubResponse(
               """{"branch":"","summary":"","error":"Build failed: CS0103 — missing dependency."}"""));

        var result = await MakeAgent(llm).RunAsync(MakeRequest());

        result.Ok.ShouldBeFalse();
        result.Repos[0].Error.ShouldNotBeNull();
        result.Repos[0].Error!.ShouldContain("CS0103");
    }

    [Fact]
    public async Task RunAsync_NoJsonInResponse_ReturnsFailure()
    {
        var llm = Substitute.For<ILlmClient>();
        llm.SendAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
           .Returns(AgentTestHelpers.StubResponse("I could not find the repository."));

        var result = await MakeAgent(llm).RunAsync(MakeRequest());

        result.Ok.ShouldBeFalse();
        result.Repos[0].Error.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RunAsync_EmptyBranchInJson_ReturnsFailure()
    {
        var llm = Substitute.For<ILlmClient>();
        llm.SendAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
           .Returns(AgentTestHelpers.StubResponse(
               """{"branch":"","summary":"Did some work","error":null}"""));

        var result = await MakeAgent(llm).RunAsync(MakeRequest());

        result.Ok.ShouldBeFalse();
        result.Repos[0].BranchName.ShouldBeEmpty();
    }

    // ── Request shape ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_DefaultProvider_IncludesRunnerShellToolNoTimeout()
    {
        LlmRequest? captured = null;
        var llm = Substitute.For<ILlmClient>();
        llm.SendAsync(Arg.Do<LlmRequest>(r => captured = r), Arg.Any<CancellationToken>())
           .Returns(AgentTestHelpers.StubResponse(
               """{"branch":"issue-7-ai-fix","summary":"Done."}"""));

        await MakeAgent(llm).RunAsync(MakeRequest(7));

        captured.ShouldNotBeNull();
        captured!.Tools.ShouldNotBeNull();
        captured.Tools!.ShouldContain("runner_shell");
        captured.Timeout.ShouldBeNull();
    }

    [Fact]
    public async Task RunAsync_RunOnMachine_UsesCliPromptWithNoServerToolsAndLongTimeout()
    {
        LlmRequest? captured = null;
        var llm = Substitute.For<ILlmClient>();
        llm.SendAsync(Arg.Do<LlmRequest>(r => captured = r), Arg.Any<CancellationToken>())
           .Returns(AgentTestHelpers.StubResponse(
               """{"branch":"issue-42-ai-fix","summary":"Done."}"""));

        // ProviderOverride = "RemoteAgent" routes the whole loop to the member's local CLI.
        var request = MakeRequest(42) with { ProviderOverride = "RemoteAgent" };
        var result = await MakeAgent(llm).RunAsync(request);

        result.Ok.ShouldBeTrue();
        captured.ShouldNotBeNull();
        // The dev-machine CLI uses its OWN tools, so no server runner_shell is exposed…
        (captured!.Tools is null || captured.Tools.Count == 0).ShouldBeTrue();
        // …a generous timeout covers the full clone→build→test→push run…
        captured.Timeout.ShouldNotBeNull();
        captured.Timeout!.Value.ShouldBeGreaterThan(TimeSpan.FromMinutes(5));
        // …and the prompt tells the CLI to clone + push itself rather than call runner_shell.
        captured.SystemPrompt.ShouldContain("clone");
        captured.SystemPrompt.ShouldContain("git push");
        captured.SystemPrompt.ShouldNotContain("runner_shell");
    }

    [Fact]
    public async Task RunAsync_PromptContainsIssueNumberAndRepo()
    {
        LlmRequest? captured = null;
        var llm = Substitute.For<ILlmClient>();
        llm.SendAsync(Arg.Do<LlmRequest>(r => captured = r), Arg.Any<CancellationToken>())
           .Returns(AgentTestHelpers.StubResponse(
               """{"branch":"issue-99-ai-fix","summary":"Done."}"""));

        await MakeAgent(llm).RunAsync(MakeRequest(99));

        captured!.SystemPrompt.ShouldContain("99");
        captured.UserPrompt.ShouldContain("99");
        captured.UserPrompt.ShouldContain("acme/my-service");
    }
}
