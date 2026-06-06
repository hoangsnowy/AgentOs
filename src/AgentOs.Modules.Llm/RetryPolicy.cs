// Exponential backoff helper (3 retries, 1s/2s/4s) for HTTP 429/5xx/timeout. Kept dependency-light
// (no Polly) so the gateway stays bootable in minimal hosts.

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Llm;
using Microsoft.Extensions.Logging;

namespace AgentOs.Modules.Llm;

/// <summary>Retry helper with exponential backoff.</summary>
public static class RetryPolicy
{
    /// <summary>Run <paramref name="operation"/> with exponential-backoff retries on transient errors.</summary>
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
                throw;
            }
            // Concrete transient types mirror IsTransient: marker 429/5xx, HTTP failures, and
            // HttpClient timeouts (TaskCanceledException raised by a request timeout, not our token —
            // that case is rethrown by the guard above). Each shares the same backoff handling.
            catch (TransientHttpException ex) { lastException = ex; if (await HandleTransientAsync(ex, attempt).ConfigureAwait(false)) { break; } }
            catch (HttpRequestException ex) { lastException = ex; if (await HandleTransientAsync(ex, attempt).ConfigureAwait(false)) { break; } }
            catch (TaskCanceledException ex) { lastException = ex; if (await HandleTransientAsync(ex, attempt).ConfigureAwait(false)) { break; } }
        }

        // Backoff for one transient attempt. Returns true when retries are exhausted (caller breaks).
        async Task<bool> HandleTransientAsync(Exception ex, int attempt)
        {
            if (attempt == maxRetries)
            {
                return true;
            }

            var wait = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * Math.Pow(2, attempt));
            logger?.LogWarning(
                "[{Provider}] Transient failure on attempt {Attempt}/{Max}: {Error}. Retrying in {Delay}ms.",
                providerName, attempt + 1, maxRetries + 1, ex.Message, wait.TotalMilliseconds);

            await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            return false;
        }

        throw new LlmException(
            $"{providerName} request failed after {maxRetries + 1} attempts.",
            providerName,
            statusCode: lastException is TransientHttpException th ? th.StatusCode : null,
            innerException: lastException);
    }

    /// <summary>True for retriable exceptions.</summary>
    public static bool IsTransient(Exception ex) => ex switch
    {
        TransientHttpException => true,
        HttpRequestException => true,
        TaskCanceledException => true,
        _ => false,
    };

    /// <summary>True for retriable HTTP statuses (429 or 5xx).</summary>
    public static bool IsRetriableStatus(HttpStatusCode status)
    {
        var code = (int)status;
        return code == 429 || (code >= 500 && code < 600);
    }
}

/// <summary>Marker exception signalling a retriable HTTP status (429/5xx).</summary>
internal sealed class TransientHttpException : Exception
{
    public int StatusCode { get; }

    public TimeSpan? RetryAfter { get; }

    public TransientHttpException(int statusCode, string message, TimeSpan? retryAfter = null) : base(message)
    {
        StatusCode = statusCode;
        RetryAfter = retryAfter;
    }
}
