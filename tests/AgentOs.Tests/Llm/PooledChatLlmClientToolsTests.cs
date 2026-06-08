// Epic E2 — Verifies that PooledChatLlmClient threads LlmRequest.Tools through to the wrapped
// IChatClient via ChatOptions.Tools (and that absent registry / absent request.Tools is a no-op).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Llm;
using AgentOs.Domain.Tools;
using AgentOs.Modules.Llm;
using AgentOs.Modules.Tools.Registry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Llm;

public sealed class PooledChatLlmClientToolsTests
{
    [Fact]
    public async Task SendAsync_NoTools_LeavesChatOptionsToolsNull()
    {
        var captured = new CapturedOptions();
        var client = BuildClient(BuildCapturingClient(captured), registry: new InMemoryToolRegistry());

        await client.SendAsync(new LlmRequest("sys", "user", "model"));

        captured.Options.ShouldNotBeNull();
        captured.Options!.Tools.ShouldBeNull();
    }

    [Fact]
    public async Task SendAsync_RequestedToolRegistered_WiresIntoChatOptions()
    {
        var registry = new InMemoryToolRegistry();
        registry.Register(FakeTool("build_verifier"));
        var captured = new CapturedOptions();
        var client = BuildClient(BuildCapturingClient(captured), registry);

        await client.SendAsync(new LlmRequest("sys", "user", "model", Tools: ["build_verifier"]));

        captured.Options!.Tools.ShouldNotBeNull();
        captured.Options.Tools!.Count.ShouldBe(1);
        captured.Options.Tools[0].ShouldBeAssignableTo<AIFunction>();
        ((AIFunction)captured.Options.Tools[0]).Name.ShouldBe("build_verifier");
    }

    [Fact]
    public async Task SendAsync_RequestedToolMissing_DroppedSilently()
    {
        var registry = new InMemoryToolRegistry();
        registry.Register(FakeTool("build_verifier"));
        var captured = new CapturedOptions();
        var client = BuildClient(BuildCapturingClient(captured), registry);

        await client.SendAsync(new LlmRequest("sys", "user", "model", Tools: ["build_verifier", "unknown_tool"]));

        captured.Options!.Tools!.Count.ShouldBe(1);
        ((AIFunction)captured.Options.Tools[0]).Name.ShouldBe("build_verifier");
    }

    [Fact]
    public async Task SendAsync_RegistryAbsent_ToolsRequestIgnored()
    {
        var captured = new CapturedOptions();
        // Registry left null — request asks for a tool but the gateway has nothing to resolve it
        // against, so ChatOptions.Tools stays null instead of crashing.
        var client = BuildClient(BuildCapturingClient(captured), registry: null);

        await client.SendAsync(new LlmRequest("sys", "user", "model", Tools: ["build_verifier"]));

        captured.Options!.Tools.ShouldBeNull();
    }

    private sealed class CapturedOptions
    {
        public ChatOptions? Options { get; set; }
    }

    private static PooledChatLlmClient BuildClient(IChatClient chat, IToolRegistry? registry)
    {
        var router = new ApiKeyRouter(TimeProvider.System);
        return new PooledChatLlmClient(
            provider: "TestProvider",
            clientFactory: (_, _) => chat,
            keyProvider: _ => ValueTask.FromResult<IReadOnlyList<string>>(new List<string> { "k1" }),
            router: router,
            isRateLimited: _ => false,
            retryAfter: _ => null,
            logger: NullLogger.Instance,
            baseDelay: TimeSpan.FromMilliseconds(1),
            toolRegistry: registry,
            tenantContext: null);
    }

    private static IChatClient BuildCapturingClient(CapturedOptions captured)
    {
        var c = Substitute.For<IChatClient>();
        c.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Do<ChatOptions>(o => captured.Options = o), Arg.Any<CancellationToken>())
            .Returns(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))
            {
                Usage = new UsageDetails { InputTokenCount = 1, OutputTokenCount = 1 },
            });
        return c;
    }

    private static ITool FakeTool(string name)
    {
        var tool = Substitute.For<ITool>();
        tool.Definition.Returns(new ToolDefinition(name, $"Fake tool {name}", """{"type":"object","properties":{}}"""));
        return tool;
    }
}
