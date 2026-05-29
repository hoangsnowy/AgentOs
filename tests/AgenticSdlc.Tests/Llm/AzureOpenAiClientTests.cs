// AgenticSdlc.Tests/Llm/AzureOpenAiClientTests.cs
// Sprint 1 — Unit tests for AzureOpenAiClient with a fake HttpMessageHandler.

using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using AgenticSdlc.Domain.Llm;
using AgenticSdlc.Infrastructure.Llm;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AgenticSdlc.Tests.Llm;

public class AzureOpenAiClientTests
{
    private static (AzureOpenAiClient client, TestHttpMessageHandler handler) BuildClient(int maxRetries = 1)
    {
        var handler = new TestHttpMessageHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.openai.azure.com/") };
        var opts = Options.Create(new LlmOptions
        {
            AzureOpenAi = new AzureOpenAiOptions
            {
                ApiKey = "test-key",
                Endpoint = "https://test.openai.azure.com",
                Model = "gpt-4.1",
                ApiVersion = "2024-10-21",
                MaxRetries = maxRetries,
                TimeoutSeconds = 60,
            },
        });
        var client = new AzureOpenAiClient(http, opts, new RuntimeOverrides(), new ApiKeyRouter(TimeProvider.System), NullLogger<AzureOpenAiClient>.Instance);
        return (client, handler);
    }

    private const string SuccessJson = """
    {
        "id": "chatcmpl-test",
        "model": "gpt-4.1",
        "choices": [
            { "index": 0, "message": { "role": "assistant", "content": "Hi back" }, "finish_reason": "stop" }
        ],
        "usage": { "prompt_tokens": 5, "completion_tokens": 10, "total_tokens": 15 }
    }
    """;

    [Fact]
    public async Task SendAsync_Success_ReturnsResponse()
    {
        var (client, handler) = BuildClient();
        handler.EnqueueResponse(HttpStatusCode.OK, SuccessJson);
        var req = new LlmRequest("sys", "Hi", "gpt-4.1");

        var res = await client.SendAsync(req);

        res.Content.ShouldBe("Hi back");
        res.InputTokens.ShouldBe(5);
        res.OutputTokens.ShouldBe(10);
        res.Provider.ShouldBe("AzureOpenAI");
        handler.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task SendAsync_429ThenSuccess_RetriesAndSucceeds()
    {
        var (client, handler) = BuildClient(maxRetries: 2);
        handler.EnqueueResponse(HttpStatusCode.TooManyRequests, "{\"error\":\"rl\"}");
        handler.EnqueueResponse(HttpStatusCode.OK, SuccessJson);
        var req = new LlmRequest("sys", "Hi", "gpt-4.1");

        var res = await client.SendAsync(req);

        res.Content.ShouldBe("Hi back");
        handler.CallCount.ShouldBe(2);
    }

    [Fact]
    public async Task SendAsync_502RetryExhausted_ThrowsLlmException()
    {
        var (client, handler) = BuildClient(maxRetries: 1);
        handler.EnqueueResponse(HttpStatusCode.BadGateway, "upstream down");
        handler.EnqueueResponse(HttpStatusCode.BadGateway, "upstream down");
        var req = new LlmRequest("sys", "Hi", "gpt-4.1");

        var ex = await Should.ThrowAsync<LlmException>(() => client.SendAsync(req));
        ex.Provider.ShouldBe("AzureOpenAI");
        handler.CallCount.ShouldBe(2);
    }

    [Fact]
    public async Task SendAsync_401Unauthorized_ThrowsImmediately()
    {
        var (client, handler) = BuildClient(maxRetries: 5);
        handler.EnqueueResponse(HttpStatusCode.Unauthorized, "{\"error\":\"bad key\"}");
        var req = new LlmRequest("sys", "Hi", "gpt-4.1");

        var ex = await Should.ThrowAsync<LlmException>(() => client.SendAsync(req));
        ex.StatusCode.ShouldBe(401);
        handler.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task SendAsync_MalformedJson_ThrowsLlmException()
    {
        var (client, handler) = BuildClient(maxRetries: 0);
        handler.EnqueueResponse(HttpStatusCode.OK, "not json at all");
        var req = new LlmRequest("sys", "Hi", "gpt-4.1");

        await Should.ThrowAsync<LlmException>(() => client.SendAsync(req));
    }
}
