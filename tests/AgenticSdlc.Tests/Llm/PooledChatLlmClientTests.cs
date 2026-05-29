// AgenticSdlc.Tests/Llm/PooledChatLlmClientTests.cs
// Unit tests for the SDK-based pooled client: round-robin + rate-limit failover across a keyed
// IChatClient pool.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Domain.Llm;
using AgenticSdlc.Modules.Llm;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace AgenticSdlc.Tests.Llm;

public class PooledChatLlmClientTests
{
    private sealed class RateLimitEx : Exception;

    private static IChatClient OkClient(string text)
    {
        var c = Substitute.For<IChatClient>();
        c.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
            {
                Usage = new UsageDetails { InputTokenCount = 1, OutputTokenCount = 1 },
            });
        return c;
    }

    private static IChatClient RateLimitedClient()
    {
        var c = Substitute.For<IChatClient>();
        c.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RateLimitEx());
        return c;
    }

    [Fact]
    public async Task SendAsync_FirstKeyRateLimited_FailsOverToNextKey()
    {
        var router = new ApiKeyRouter(TimeProvider.System);
        var map = new Dictionary<string, IChatClient> { ["k1"] = RateLimitedClient(), ["k2"] = OkClient("ok") };

        var client = new PooledChatLlmClient(
            "P", (key, _) => map[key], () => new List<string> { "k1", "k2" }, router,
            ex => ex is RateLimitEx, _ => null, NullLogger.Instance, TimeSpan.FromMilliseconds(1));

        var result = await client.SendAsync(new LlmRequest("s", "u", "m"));
        result.Content.ShouldBe("ok");
    }

    [Fact]
    public async Task SendAsync_AllKeysRateLimited_ThrowsLlmException()
    {
        var router = new ApiKeyRouter(TimeProvider.System);
        var limited = RateLimitedClient();

        var client = new PooledChatLlmClient(
            "P", (_, _) => limited, () => new List<string> { "k1", "k2" }, router,
            ex => ex is RateLimitEx, _ => null, NullLogger.Instance, TimeSpan.FromMilliseconds(1));

        await Should.ThrowAsync<LlmException>(() => client.SendAsync(new LlmRequest("s", "u", "m")));
    }

    [Fact]
    public async Task SendAsync_NoKeys_ThrowsLlmException()
    {
        var router = new ApiKeyRouter(TimeProvider.System);
        var client = new PooledChatLlmClient(
            "P", (_, _) => OkClient("x"), () => new List<string>(), router,
            _ => false, _ => null, NullLogger.Instance);

        await Should.ThrowAsync<LlmException>(() => client.SendAsync(new LlmRequest("s", "u", "m")));
    }
}
