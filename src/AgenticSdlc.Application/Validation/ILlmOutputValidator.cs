// AgenticSdlc.Application/Validation/ILlmOutputValidator.cs
// Sprint 3 — validate LLM output JSON theo schema name đăng ký sẵn.

namespace AgenticSdlc.Application.Validation;

/// <summary>Validate raw JSON string của LLM output theo named schema.</summary>
public interface ILlmOutputValidator
{
    /// <summary>
    /// Validate <paramref name="json"/> theo schema được đăng ký bằng <paramref name="schemaName"/>.
    /// Ném <see cref="LlmOutputValidationException"/> nếu fail.
    /// </summary>
    /// <param name="json">JSON output (raw text từ LLM, sau khi strip markdown fence).</param>
    /// <param name="schemaName">Tên schema (vd "requirement-spec.v1", "code-artifact.v1", "test-artifact.v1").</param>
    /// <param name="agentName">Tên agent gọi (dùng cho error message).</param>
    void Validate(string json, string schemaName, string agentName);
}
