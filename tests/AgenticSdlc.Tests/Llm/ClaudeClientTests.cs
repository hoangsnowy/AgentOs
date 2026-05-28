// AgenticSdlc.Tests/Llm/ClaudeClientTests.cs
// Sprint 1 — Unit tests for ClaudeClient with a fake HttpMessageHandler.

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

public class ClaudeClientTests
{
    private static (ClaudeClient client, TestHttpMessageHandler handler) BuildClient(int maxRetries = 1, int timeoutSeconds = 60)
    {
        var handler = new TestHttpMessageHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.test/") };
        var opts = Options.Create(new LlmOptions
        {
            Claude = new ClaudeOptions
            {
                ApiKey = "test-key",
                Endpoint = "https://api.anthropic.test",
                ApiVersion = "2023-06-01",
                MaxRetries = maxRetries,
                TimeoutSeconds = timeoutSeconds,
            },
        });
        var client = new ClaudeClient(http, opts, new RuntimeOverrides(), NullLogger<ClaudeClient>.Instance);
        return (client, handler);
    }

    private const string SuccessJson = """
    {
        "id": "msg_test",
        "model": "claude-sonnet-4-20250514",
        "content": [ { "type": "text", "text": "Hello, world!" } ],
        "usage": { "input_tokens": 12, "output_tokens": 34 }
    }
    """;

    [Fact]
    public async Task SendAsync_Success_ReturnsResponse()
    {
        // Arrange
        var (client, handler) = BuildClient();
        handler.EnqueueResponse(HttpStatusCode.OK, SuccessJson);
        var req = new LlmRequest("sys", "Hi", "claude-sonnet-4-20250514");

        // Act
        var res = await client.SendAsync(req);

        // Assert
        res.Content.ShouldBe("Hello, world!");
        res.InputTokens.ShouldBe(12);
        res.OutputTokens.ShouldBe(34);
        res.Provider.ShouldBe("Claude");
        res.CostUsd.ShouldBeGreaterThan(0m);
        handler.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task SendAsync_429ThenSuccess_RetriesAndSucceeds()
    {
        // Arrange — 1 retry: first call 429, second OK.
        var (client, handler) = BuildClient(maxRetries: 2);
        handler.EnqueueResponse(HttpStatusCode.TooManyRequests, "{\"error\":\"rate limit\"}");
        handler.EnqueueResponse(HttpStatusCode.OK, SuccessJson);
        var req = new LlmRequest("sys", "Hi", "claude-sonnet-4-20250514");

        // Act
        var res = await client.SendAsync(req);

        // Assert
        res.Content.ShouldBe("Hello, world!");
        handler.CallCount.ShouldBe(2);
    }

    [Fact]
    public async Task SendAsync_500RetryExhausted_ThrowsLlmException()
    {
        // Arrange — 0 retries (single attempt), returns 500.
        var (client, handler) = BuildClient(maxRetries: 0);
        handler.EnqueueResponse(HttpStatusCode.InternalServerError, "boom");
        var req = new LlmRequest("sys", "Hi", "claude-sonnet-4-20250514");

        // Act & Assert
        var ex = await Should.ThrowAsync<LlmException>(() => client.SendAsync(req));
        ex.Provider.ShouldBe("Claude");
        handler.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task SendAsync_400NonRetriable_ThrowsImmediately()
    {
        // Arrange — 400 is not retried. Configure a high MaxRetries but still only 1 call.
        var (client, handler) = BuildClient(maxRetries: 5);
        handler.EnqueueResponse(HttpStatusCode.BadRequest, "{\"error\":\"bad request\"}");
        var req = new LlmRequest("sys", "Hi", "claude-sonnet-4-20250514");

        // Act & Assert
        var ex = await Should.ThrowAsync<LlmException>(() => client.SendAsync(req));
        ex.StatusCode.ShouldBe(400);
        handler.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task SendAsync_MalformedJson_ThrowsLlmException()
    {
        // Arrange
        var (client, handler) = BuildClient(maxRetries: 0);
        handler.EnqueueResponse(HttpStatusCode.OK, "{not valid json");
        var req = new LlmRequest("sys", "Hi", "claude-sonnet-4-20250514");

        // Act & Assert
        var ex = await Should.ThrowAsync<LlmException>(() => client.SendAsync(req));
        ex.Provider.ShouldBe("Claude");
    }
}
