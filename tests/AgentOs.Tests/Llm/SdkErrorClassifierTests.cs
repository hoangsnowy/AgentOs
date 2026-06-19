// Error classification that drives the pooled client's key-failover loop. Transient (429/5xx/timeout) +
// Auth (401/403) warrant a key retry; BadRequest (4xx-malformed) + Other are non-retryable. The typed
// ClientResultException (Azure) status path is exercised via the equivalent HttpRequestException status
// path + the message heuristics that catch both SDK shapes.

using System;
using System.Net;
using System.Net.Http;
using AgentOs.Modules.Llm;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Llm;

public sealed class SdkErrorClassifierTests
{
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, LlmErrorKind.Transient)]   // 429
    [InlineData(HttpStatusCode.RequestTimeout, LlmErrorKind.Transient)]    // 408
    [InlineData(HttpStatusCode.InternalServerError, LlmErrorKind.Transient)] // 500
    [InlineData(HttpStatusCode.BadGateway, LlmErrorKind.Transient)]        // 502
    [InlineData(HttpStatusCode.ServiceUnavailable, LlmErrorKind.Transient)] // 503
    [InlineData(HttpStatusCode.GatewayTimeout, LlmErrorKind.Transient)]    // 504
    [InlineData(HttpStatusCode.Unauthorized, LlmErrorKind.Auth)]           // 401
    [InlineData(HttpStatusCode.Forbidden, LlmErrorKind.Auth)]              // 403
    [InlineData(HttpStatusCode.BadRequest, LlmErrorKind.BadRequest)]       // 400
    [InlineData(HttpStatusCode.NotFound, LlmErrorKind.BadRequest)]         // 404
    [InlineData(HttpStatusCode.UnprocessableEntity, LlmErrorKind.BadRequest)] // 422
    public void Classify_HttpStatus_MapsToKind(HttpStatusCode status, LlmErrorKind expected)
    {
        SdkChatClients.Classify(new HttpRequestException("x", null, status)).ShouldBe(expected);
    }

    [Fact]
    public void Classify_TimeoutAndTransport_AreTransient()
    {
        SdkChatClients.Classify(new TaskCanceledException("timed out")).ShouldBe(LlmErrorKind.Transient);
        SdkChatClients.Classify(new TimeoutException()).ShouldBe(LlmErrorKind.Transient);
        SdkChatClients.Classify(new System.Net.Sockets.SocketException()).ShouldBe(LlmErrorKind.Transient);
    }

    [Theory]
    [InlineData("Error: overloaded_error", LlmErrorKind.Transient)]
    [InlineData("HTTP 429 too many requests", LlmErrorKind.Transient)]
    [InlineData("401 Unauthorized: invalid x-api-key", LlmErrorKind.Auth)]
    [InlineData("authentication_error: invalid api key", LlmErrorKind.Auth)]
    [InlineData("503 Service Unavailable", LlmErrorKind.Transient)]
    public void Classify_MessageHeuristics_MapWhenNoTypedStatus(string message, LlmErrorKind expected)
    {
        SdkChatClients.Classify(new InvalidOperationException(message)).ShouldBe(expected);
    }

    [Fact]
    public void Classify_UnknownError_IsOther()
    {
        SdkChatClients.Classify(new InvalidOperationException("something unexpected")).ShouldBe(LlmErrorKind.Other);
    }

    [Fact]
    public void Classify_WalksInnerExceptionChain()
    {
        var inner = new HttpRequestException("x", null, HttpStatusCode.ServiceUnavailable);
        SdkChatClients.Classify(new InvalidOperationException("wrapper", inner)).ShouldBe(LlmErrorKind.Transient);
    }

    [Fact]
    public void IsRateLimited_StaysNarrow_429Only()
    {
        SdkChatClients.IsRateLimited(new HttpRequestException("x", null, HttpStatusCode.TooManyRequests)).ShouldBeTrue();
        // A 5xx is transient-for-failover but NOT a rate-limit — the legacy helper must not widen.
        SdkChatClients.IsRateLimited(new HttpRequestException("x", null, HttpStatusCode.ServiceUnavailable)).ShouldBeFalse();
    }
}
