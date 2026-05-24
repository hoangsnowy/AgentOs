// AgenticSdlc.Domain/Llm/LlmException.cs
// Sprint 1 — Custom exception for the LLM Gateway.

namespace AgenticSdlc.Domain.Llm;

/// <summary>
/// Exception thrown when the LLM client hits an unrecoverable error (retries exhausted, malformed response, etc.).
/// Upstream code (agents, orchestrator) catches this specific exception to distinguish it from business errors.
/// </summary>
[System.Serializable]
public class LlmException : System.Exception
{
    /// <summary>Name of the provider that caused the error (<c>"Claude"</c>, <c>"AzureOpenAI"</c>, ...).</summary>
    public string? Provider { get; }

    /// <summary>HTTP status code (if the error originated from the HTTP transport).</summary>
    public int? StatusCode { get; }

    /// <inheritdoc />
    public LlmException()
    {
    }

    /// <inheritdoc />
    public LlmException(string message) : base(message)
    {
    }

    /// <inheritdoc />
    public LlmException(string message, System.Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes with provider information + status code.
    /// </summary>
    public LlmException(string message, string provider, int? statusCode = null, System.Exception? innerException = null)
        : base(message, innerException)
    {
        Provider = provider;
        StatusCode = statusCode;
    }
}
