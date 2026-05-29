// AgenticSdlc.Tests/Llm/RetryPolicyTests.cs
// Sprint 1 — Unit test exponential backoff retry helper.

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Domain.Llm;
using AgenticSdlc.Modules.Llm;
using Shouldly;
using Xunit;

namespace AgenticSdlc.Tests.Llm;

public class RetryPolicyTests
{
    [Fact]
    public async Task ExecuteAsync_FirstAttemptSucceeds_ReturnsResult()
    {
        // Arrange
        var calls = 0;

        // Act
        var result = await RetryPolicy.ExecuteAsync<int>(
            operation: _ => { calls++; return Task.FromResult(42); },
            maxRetries: 3,
            baseDelay: TimeSpan.FromMilliseconds(1),
            providerName: "Test");

        // Assert
        result.ShouldBe(42);
        calls.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_TransientThenSuccess_RetriesAndReturns()
    {
        // Arrange
        var calls = 0;
        Func<CancellationToken, Task<string>> op = _ =>
        {
            calls++;
            if (calls == 1)
            {
                throw new HttpRequestException("transient");
            }
            return Task.FromResult("ok");
        };

        // Act
        var result = await RetryPolicy.ExecuteAsync(
            op,
            maxRetries: 2,
            baseDelay: TimeSpan.FromMilliseconds(1),
            providerName: "Test");

        // Assert
        result.ShouldBe("ok");
        calls.ShouldBe(2);
    }

    [Fact]
    public async Task ExecuteAsync_RetryExhausted_ThrowsLlmException()
    {
        // Arrange
        var calls = 0;
        Func<CancellationToken, Task<int>> op = _ =>
        {
            calls++;
            throw new HttpRequestException("always fails");
        };

        // Act & Assert
        var ex = await Should.ThrowAsync<LlmException>(() => RetryPolicy.ExecuteAsync(
            op,
            maxRetries: 2,
            baseDelay: TimeSpan.FromMilliseconds(1),
            providerName: "Test"));

        ex.Provider.ShouldBe("Test");
        calls.ShouldBe(3); // 1 initial + 2 retries
    }

    [Fact]
    public void IsRetriableStatus_429And5xx_ReturnTrue()
    {
        RetryPolicy.IsRetriableStatus(System.Net.HttpStatusCode.TooManyRequests).ShouldBeTrue();
        RetryPolicy.IsRetriableStatus(System.Net.HttpStatusCode.InternalServerError).ShouldBeTrue();
        RetryPolicy.IsRetriableStatus(System.Net.HttpStatusCode.BadGateway).ShouldBeTrue();
        RetryPolicy.IsRetriableStatus((System.Net.HttpStatusCode)503).ShouldBeTrue();
    }

    [Fact]
    public void IsRetriableStatus_4xxNonRateLimit_ReturnsFalse()
    {
        RetryPolicy.IsRetriableStatus(System.Net.HttpStatusCode.BadRequest).ShouldBeFalse();
        RetryPolicy.IsRetriableStatus(System.Net.HttpStatusCode.Unauthorized).ShouldBeFalse();
        RetryPolicy.IsRetriableStatus(System.Net.HttpStatusCode.NotFound).ShouldBeFalse();
    }
}
