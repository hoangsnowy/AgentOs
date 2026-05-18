// AgenticSdlc.Domain/Llm/LlmException.cs
// Sprint 1 — Custom exception cho LLM Gateway.

namespace AgenticSdlc.Domain.Llm;

/// <summary>
/// Exception ném khi LLM client gặp lỗi không thể xử lý (đã hết retry, malformed response, v.v.).
/// Upstream code (agents, orchestrator) bắt cụ thể exception này để phân biệt với lỗi business.
/// </summary>
[System.Serializable]
public class LlmException : System.Exception
{
    /// <summary>Tên provider gây ra lỗi (<c>"Claude"</c>, <c>"AzureOpenAI"</c>, ...).</summary>
    public string? Provider { get; }

    /// <summary>HTTP status code (nếu lỗi tới từ HTTP transport).</summary>
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
    /// Khởi tạo với thông tin provider + status code.
    /// </summary>
    public LlmException(string message, string provider, int? statusCode = null, System.Exception? innerException = null)
        : base(message, innerException)
    {
        Provider = provider;
        StatusCode = statusCode;
    }
}
