// AgenticSdlc.Application/Validation/LlmOutputValidationException.cs
// Sprint 3 — exception khi LLM output JSON không khớp JSON Schema.

using System;
using System.Collections.Generic;
using AgenticSdlc.Domain.Llm;

namespace AgenticSdlc.Application.Validation;

/// <summary>Output JSON của LLM không khớp JSON Schema.</summary>
public sealed class LlmOutputValidationException : LlmException
{
    /// <summary>Danh sách lỗi: mỗi item = "&lt;property path&gt;: &lt;reason&gt;".</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Schema URI bị vi phạm.</summary>
    public string SchemaUri { get; }

    /// <summary>Khởi tạo.</summary>
    public LlmOutputValidationException(string agentName, string schemaUri, IReadOnlyList<string> errors)
        : base(BuildMessage(agentName, schemaUri, errors), agentName)
    {
        ArgumentNullException.ThrowIfNull(errors);
        SchemaUri = schemaUri ?? throw new ArgumentNullException(nameof(schemaUri));
        Errors = errors;
    }

    private static string BuildMessage(string agentName, string schemaUri, IReadOnlyList<string> errors)
    {
        var head = $"{agentName}: LLM output failed JSON Schema validation ({schemaUri}). {errors.Count} error(s):";
        if (errors.Count == 0)
        {
            return head;
        }
        return head + "\n - " + string.Join("\n - ", errors);
    }
}
