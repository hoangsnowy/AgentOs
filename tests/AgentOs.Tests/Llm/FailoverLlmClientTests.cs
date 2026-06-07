// Unit tests for the inter-provider failover chain (FailoverLlmClient). Proves it advances to the next
// provider only on a provider-level LlmException, reports the primary's name, and propagates the final
// failure when the whole chain is exhausted.

using System;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Llm;
using AgentOs.Modules.Llm;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Llm;

public sealed class FailoverLlmClientTests
{
    private static LlmRequest Req() => new("sys", "user", "model", 0.2, 100);

    private static LlmResponse Ok(string provider) =>
        new("ok", 1, 1, 0m, TimeSpan.Zero, "model", provider);

    private sealed class StubClient(string provider, Func<LlmResponse> onCall) : ILlmClient
    {
        public string Provider { get; } = provider;
        public int Calls { get; private set; }

        public Task<LlmResponse> SendAsync(LlmRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(onCall());
        }
    }

    private static StubClient Throwing(string provider) =>
        new(provider, () => throw new LlmException($"{provider} exhausted", provider));

    [Fact]
    public async Task SendAsync_PrimaryThrows_FailsOverToSecondary()
    {
        var primary = Throwing("Claude");
        var secondary = new StubClient("AzureOpenAI", () => Ok("AzureOpenAI"));
        var sut = new FailoverLlmClient([primary, secondary], NullLogger.Instance);

        var resp = await sut.SendAsync(Req());

        resp.Provider.ShouldBe("AzureOpenAI");
        primary.Calls.ShouldBe(1);
        secondary.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task SendAsync_PrimarySucceeds_DoesNotCallSecondary()
    {
        var primary = new StubClient("Claude", () => Ok("Claude"));
        var secondary = new StubClient("AzureOpenAI", () => Ok("AzureOpenAI"));
        var sut = new FailoverLlmClient([primary, secondary], NullLogger.Instance);

        var resp = await sut.SendAsync(Req());

        resp.Provider.ShouldBe("Claude");
        secondary.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task SendAsync_AllProvidersThrow_PropagatesLastLlmException()
    {
        var sut = new FailoverLlmClient([Throwing("Claude"), Throwing("AzureOpenAI")], NullLogger.Instance);

        var ex = await Should.ThrowAsync<LlmException>(async () => await sut.SendAsync(Req()));

        ex.Message.ShouldContain("AzureOpenAI");
    }

    [Fact]
    public void Provider_ReportsThePrimary()
    {
        var sut = new FailoverLlmClient([new StubClient("Claude", () => Ok("Claude"))], NullLogger.Instance);

        sut.Provider.ShouldBe("Claude");
    }
}
