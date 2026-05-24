// AgenticSdlc.Tests/Llm/MockLlmClientTests.cs
// Sprint 1 — Unit tests for MockLlmClient (fixture load + stub fallback).

using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AgenticSdlc.Domain.Llm;
using AgenticSdlc.Infrastructure.Llm;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AgenticSdlc.Tests.Llm;

public class MockLlmClientTests : IDisposable
{
    private readonly string _tempDir;

    public MockLlmClientTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "agentic-sdlc-mock-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private MockLlmClient CreateClient()
    {
        var opts = Options.Create(new LlmOptions
        {
            Mock = new MockOptions
            {
                FixturePath = _tempDir,
                SimulatedLatencyMs = 0,
            },
        });
        return new MockLlmClient(opts, NullLogger<MockLlmClient>.Instance);
    }

    [Fact]
    public async Task SendAsync_FixtureMiss_ReturnsStubResponse()
    {
        // Arrange
        var client = CreateClient();
        var req = new LlmRequest("sys", "no-fixture-here", "claude-sonnet-4");

        // Act
        var res = await client.SendAsync(req);

        // Assert
        res.Content.ShouldBe("stub-response");
        res.Provider.ShouldBe("Mock");
        res.Model.ShouldBe("claude-sonnet-4");
    }

    [Fact]
    public async Task SendAsync_FixtureHit_LoadsFixtureContent()
    {
        // Arrange
        var client = CreateClient();
        var req = new LlmRequest("sys", "load me", "claude-sonnet-4");
        var hash = MockLlmClient.ComputeHash(req);
        var fixturePath = Path.Combine(_tempDir, $"{hash}.json");
        var fixture = new
        {
            content = "loaded-from-fixture",
            inputTokens = 42,
            outputTokens = 7,
        };
        await File.WriteAllTextAsync(fixturePath, JsonSerializer.Serialize(fixture));

        // Act
        var res = await client.SendAsync(req);

        // Assert
        res.Content.ShouldBe("loaded-from-fixture");
        res.InputTokens.ShouldBe(42);
        res.OutputTokens.ShouldBe(7);
    }

    [Fact]
    public async Task SendAsync_LatencyMeasured_Positive()
    {
        // Arrange — set latency to 5ms to ensure the Stopwatch captures > 0.
        var opts = Options.Create(new LlmOptions
        {
            Mock = new MockOptions { FixturePath = _tempDir, SimulatedLatencyMs = 5 },
        });
        var client = new MockLlmClient(opts, NullLogger<MockLlmClient>.Instance);
        var req = new LlmRequest("sys", "ping", "gpt-4o-mini");

        // Act
        var res = await client.SendAsync(req);

        // Assert
        res.Latency.TotalMilliseconds.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void ComputeHash_SameRequest_SameHash()
    {
        // Arrange
        var a = new LlmRequest("sys", "user", "model-x");
        var b = new LlmRequest("sys", "user", "model-x");

        // Act
        var ha = MockLlmClient.ComputeHash(a);
        var hb = MockLlmClient.ComputeHash(b);

        // Assert
        ha.ShouldBe(hb);
        ha.Length.ShouldBe(16);
    }

    [Fact]
    public void ComputeHash_DifferentRequest_DifferentHash()
    {
        var a = new LlmRequest("sys", "user", "model-x");
        var b = new LlmRequest("sys", "user", "model-y");

        MockLlmClient.ComputeHash(a).ShouldNotBe(MockLlmClient.ComputeHash(b));
    }
}
