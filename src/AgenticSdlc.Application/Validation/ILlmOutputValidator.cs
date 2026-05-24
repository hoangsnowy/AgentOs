// AgenticSdlc.Application/Validation/ILlmOutputValidator.cs
// Sprint 3 — validates LLM output JSON against a pre-registered schema name.

namespace AgenticSdlc.Application.Validation;

/// <summary>Validates the raw JSON string of LLM output against a named schema.</summary>
public interface ILlmOutputValidator
{
    /// <summary>
    /// Validates <paramref name="json"/> against the schema registered under <paramref name="schemaName"/>.
    /// Throws <see cref="LlmOutputValidationException"/> on failure.
    /// </summary>
    /// <param name="json">JSON output (raw text from the LLM, after stripping the markdown fence).</param>
    /// <param name="schemaName">Schema name (e.g. "requirement-spec.v1", "code-artifact.v1", "test-artifact.v1").</param>
    /// <param name="agentName">Name of the calling agent (used for the error message).</param>
    void Validate(string json, string schemaName, string agentName);
}
