// AgenticSdlc.Tests/Llm/ChatClientLlmClientTests.cs
// Unit tests for the IChatClient -> ILlmClient adapter (the SDK-based gateway substrate).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Domain.Llm;
using AgenticSdlc.Infrastructure.Llm;
using Microsoft.Extensions.AI;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace AgenticSdlc.Tests.Llm;

public class ChatClientLlmClientTests
{
    [Fact]
    public async Task SendAsync_MapsChatResponse_ToLlmResponse()
    {
        var chat = Substitute.For<IChatClient>();
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "generated code"))
        {
            Usage = new UsageDetails { InputTokenCount = 12, OutputTokenCount = 7 },
        };
        chat.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(response);

        var client = new ChatClientLlmClient(chat, "TestProvider");
        var result = await client.SendAsync(new LlmRequest("sys", "user", "model-x"));

        result.Content.ShouldBe("generated code");
        result.InputTokens.ShouldBe(12);
        result.OutputTokens.ShouldBe(7);
        result.Provider.ShouldBe("TestProvider");
        result.Model.ShouldBe("model-x");
    }

    [Fact]
    public async Task SendAsync_ChatClientThrows_WrapsInLlmException()
    {
        var chat = Substitute.For<IChatClient>();
        chat.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));

        var client = new ChatClientLlmClient(chat, "TestProvider");

        await Should.ThrowAsync<LlmException>(() => client.SendAsync(new LlmRequest("sys", "user", "model")));
    }
}
