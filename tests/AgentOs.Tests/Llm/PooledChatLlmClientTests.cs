// AgentOs.Tests/Llm/PooledChatLlmClientTests.cs
// Unit tests for the SDK-based pooled client: round-robin + rate-limit failover across a keyed
// IChatClient pool.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Llm;
using AgentOs.Modules.Llm;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Llm;

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

    private static IChatClient ThrowingClient(Exception ex)
    {
        var c = Substitute.For<IChatClient>();
        c.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(ex);
        return c;
    }

    // Production wires SdkChatClients.Classify so the pool fails over on transient (5xx/timeout) + auth,
    // not only 429.
    [Fact]
    public async Task SendAsync_FirstKeyTransient5xx_FailsOverToNextKey()
    {
        var router = new ApiKeyRouter(TimeProvider.System);
        var map = new Dictionary<string, IChatClient>
        {
            ["k1"] = ThrowingClient(new HttpRequestException("boom", null, HttpStatusCode.ServiceUnavailable)),
            ["k2"] = OkClient("ok"),
        };

        var client = new PooledChatLlmClient(
            "P", (key, _) => map[key], _ => ValueTask.FromResult<IReadOnlyList<string>>(new List<string> { "k1", "k2" }), router,
            SdkChatClients.IsRateLimited, _ => null, NullLogger.Instance, TimeSpan.FromMilliseconds(1),
            classifyError: SdkChatClients.Classify);

        (await client.SendAsync(new LlmRequest("s", "u", "m"))).Content.ShouldBe("ok");
    }

    [Fact]
    public async Task SendAsync_FirstKeyAuthError_FailsOverToNextKey()
    {
        var router = new ApiKeyRouter(TimeProvider.System);
        var map = new Dictionary<string, IChatClient>
        {
            ["k1"] = ThrowingClient(new HttpRequestException("nope", null, HttpStatusCode.Unauthorized)),
            ["k2"] = OkClient("ok"),
        };

        var client = new PooledChatLlmClient(
            "P", (key, _) => map[key], _ => ValueTask.FromResult<IReadOnlyList<string>>(new List<string> { "k1", "k2" }), router,
            SdkChatClients.IsRateLimited, _ => null, NullLogger.Instance, TimeSpan.FromMilliseconds(1),
            classifyError: SdkChatClients.Classify);

        (await client.SendAsync(new LlmRequest("s", "u", "m"))).Content.ShouldBe("ok");
    }

    [Fact]
    public async Task SendAsync_BadRequest_DoesNotFailOver_PropagatesRaw()
    {
        var router = new ApiKeyRouter(TimeProvider.System);
        var k2 = OkClient("ok");
        var map = new Dictionary<string, IChatClient>
        {
            ["k1"] = ThrowingClient(new HttpRequestException("bad", null, HttpStatusCode.BadRequest)),
            ["k2"] = k2,
        };

        var client = new PooledChatLlmClient(
            "P", (key, _) => map[key], _ => ValueTask.FromResult<IReadOnlyList<string>>(new List<string> { "k1", "k2" }), router,
            SdkChatClients.IsRateLimited, _ => null, NullLogger.Instance, TimeSpan.FromMilliseconds(1),
            classifyError: SdkChatClients.Classify);

        // A 400 is non-retryable — the same payload would just 400 on k2 — so it propagates without failover.
        await Should.ThrowAsync<HttpRequestException>(() => client.SendAsync(new LlmRequest("s", "u", "m")));
        await k2.DidNotReceive().GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_FirstKeyRateLimited_FailsOverToNextKey()
    {
        var router = new ApiKeyRouter(TimeProvider.System);
        var map = new Dictionary<string, IChatClient> { ["k1"] = RateLimitedClient(), ["k2"] = OkClient("ok") };

        var client = new PooledChatLlmClient(
            "P", (key, _) => map[key], _ => ValueTask.FromResult<IReadOnlyList<string>>(new List<string> { "k1", "k2" }), router,
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
            "P", (_, _) => limited, _ => ValueTask.FromResult<IReadOnlyList<string>>(new List<string> { "k1", "k2" }), router,
            ex => ex is RateLimitEx, _ => null, NullLogger.Instance, TimeSpan.FromMilliseconds(1));

        await Should.ThrowAsync<LlmException>(() => client.SendAsync(new LlmRequest("s", "u", "m")));
    }

    [Fact]
    public async Task SendAsync_NoKeys_ThrowsLlmException()
    {
        var router = new ApiKeyRouter(TimeProvider.System);
        var client = new PooledChatLlmClient(
            "P", (_, _) => OkClient("x"), _ => ValueTask.FromResult<IReadOnlyList<string>>(new List<string>()), router,
            _ => false, _ => null, NullLogger.Instance);

        await Should.ThrowAsync<LlmException>(() => client.SendAsync(new LlmRequest("s", "u", "m")));
    }
}
