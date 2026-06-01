// M5 — unit tests for IssueWorkAgent.
// Uses a mock ILlmClient so no real LLM calls are made.

using System;
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
        WorkspaceOwner: "acme",
        WorkspaceRepo: "my-service",
        WorkspaceDefaultBranch: "main",
        IssueNumber: issueNumber,
        IssueTitle: $"Bug #{issueNumber}: Something is broken",
        IssueBody: "When X happens, Y fails.");

    private static IssueWorkAgent MakeAgent(ILlmClient llm) =>
        new(AgentTestHelpers.FactoryReturning(llm),
            AgentTestHelpers.OptionsWith(new AgentsOptions()),
            NullLogger<IssueWorkAgent>.Instance);

    // ── Success path ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_SuccessJson_ReturnsBranchAndSummary()
    {
        var llm = Substitute.For<ILlmClient>();
        llm.SendAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
           .Returns(AgentTestHelpers.StubResponse(
               """{"branch":"issue-42-ai-fix","summary":"Replaced null check with guard clause."}"""));

        var result = await MakeAgent(llm).RunAsync(MakeRequest());

        result.Ok.ShouldBeTrue();
        result.BranchName.ShouldBe("issue-42-ai-fix");
        result.Summary.ShouldBe("Replaced null check with guard clause.");
        result.Error.ShouldBeNull();
    }

    [Fact]
    public async Task RunAsync_JsonEmbeddedInText_ExtractsLastJsonBlock()
    {
        // Agent might emit reasoning before the JSON.
        var response = "I explored the repo and found the issue.\n" +
                       "{\"branch\":\"issue-42-ai-fix\",\"summary\":\"Added missing null check.\"}";

        var llm = Substitute.For<ILlmClient>();
        llm.SendAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
           .Returns(AgentTestHelpers.StubResponse(response));

        var result = await MakeAgent(llm).RunAsync(MakeRequest());

        result.Ok.ShouldBeTrue();
        result.BranchName.ShouldBe("issue-42-ai-fix");
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
        result.Error.ShouldNotBeNull();
        result.Error!.ShouldContain("CS0103");
    }

    [Fact]
    public async Task RunAsync_NoJsonInResponse_ReturnsFailure()
    {
        var llm = Substitute.For<ILlmClient>();
        llm.SendAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
           .Returns(AgentTestHelpers.StubResponse("I could not find the repository."));

        var result = await MakeAgent(llm).RunAsync(MakeRequest());

        result.Ok.ShouldBeFalse();
        result.Error.ShouldNotBeNullOrWhiteSpace();
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
        result.BranchName.ShouldBeEmpty();
    }

    // ── Request shape ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_AlwaysIncludesRunnerShellTool()
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
    }

    [Fact]
    public async Task RunAsync_SystemPromptContainsIssueNumber()
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
