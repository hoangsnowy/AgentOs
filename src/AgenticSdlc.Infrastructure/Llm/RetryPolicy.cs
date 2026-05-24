// AgenticSdlc.Infrastructure/Llm/RetryPolicy.cs
// Sprint 1 — Exponential backoff helper (3 retries, 1s/2s/4s) for HTTP 429/5xx/timeout.

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AgenticSdlc.Infrastructure.Llm;

/// <summary>
/// Retry helper with exponential backoff. Does NOT use Polly, to keep dependencies minimal in Sprint 1
/// (Polly can be added later via <c>Microsoft.Extensions.Http.Resilience</c> if needed).
/// </summary>
public static class RetryPolicy
{
    /// <summary>
    /// Runs <paramref name="operation"/> with retries. Retries only on:
    /// <list type="bullet">
    ///   <item><see cref="HttpRequestException"/></item>
    ///   <item><see cref="TaskCanceledException"/> not caused by <paramref name="cancellationToken"/></item>
    ///   <item><see cref="HttpResponseMessage"/> with status 429 or 5xx (the operation throws when it checks)</item>
    /// </list>
    /// </summary>
    /// <typeparam name="T">Result type.</typeparam>
    /// <param name="operation">Async function that performs a single call.</param>
    /// <param name="maxRetries">Maximum number of retries (not counting the first attempt).</param>
    /// <param name="baseDelay">Base delay (default 1s). The k-th attempt delay = baseDelay * 2^(k-1).</param>
    /// <param name="logger">Logger (optional).</param>
    /// <param name="providerName">Provider name for log/exception.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the successful call.</returns>
    /// <exception cref="Domain.Llm.LlmException">Thrown when retries are exhausted.</exception>
    public static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        int maxRetries,
        TimeSpan? baseDelay = null,
        ILogger? logger = null,
        string providerName = "Llm",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (maxRetries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRetries), "maxRetries must be >= 0.");
        }

        var delay = baseDelay ?? TimeSpan.FromSeconds(1);
        Exception? lastException = null;

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // User cancelled — do not retry, rethrow immediately.
                throw;
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                lastException = ex;

                if (attempt == maxRetries)
                {
                    break;
                }

                var wait = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * Math.Pow(2, attempt));
                logger?.LogWarning(
                    "[{Provider}] Transient failure on attempt {Attempt}/{Max}: {Error}. Retrying in {Delay}ms.",
                    providerName, attempt + 1, maxRetries + 1, ex.Message, wait.TotalMilliseconds);

                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new Domain.Llm.LlmException(
            $"{providerName} request failed after {maxRetries + 1} attempts.",
            providerName,
            statusCode: lastException is TransientHttpException th ? th.StatusCode : null,
            innerException: lastException);
    }

    /// <summary>
    /// Returns true if the exception is of a retriable kind.
    /// Includes <see cref="HttpRequestException"/>, <see cref="TaskCanceledException"/> (timeout),
    /// and the custom <see cref="TransientHttpException"/>.
    /// </summary>
    public static bool IsTransient(Exception ex) => ex switch
    {
        TransientHttpException => true,
        HttpRequestException => true,
        TaskCanceledException => true, // HttpClient throws this on timeout
        _ => false,
    };

    /// <summary>
    /// Returns true if the HTTP status is worth retrying (429 or 5xx).
    /// </summary>
    public static bool IsRetriableStatus(HttpStatusCode status)
    {
        var code = (int)status;
        return code == 429 || (code >= 500 && code < 600);
    }
}

/// <summary>
/// Marker exception used internally in <see cref="RetryPolicy"/> to signal an HTTP status worth retrying
/// (e.g. 429 or 5xx). The caller (ClaudeClient, AzureOpenAiClient) throws this exception when it encounters
/// a retriable status code, so <see cref="RetryPolicy.ExecuteAsync"/> can catch it and retry.
/// </summary>
internal sealed class TransientHttpException : Exception
{
    public int StatusCode { get; }

    public TransientHttpException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}
