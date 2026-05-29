// AgenticSdlc.Application/Validation/LlmOutputValidationException.cs
// Sprint 3 — exception thrown when LLM output JSON does not match the JSON Schema.

using System;
using System.Collections.Generic;
using AgenticSdlc.Domain.Llm;

namespace AgenticSdlc.Modules.Pipeline.Validation;

/// <summary>The LLM's output JSON does not match the JSON Schema.</summary>
public sealed class LlmOutputValidationException : LlmException
{
    /// <summary>List of errors: each item = "&lt;property path&gt;: &lt;reason&gt;".</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>The violated schema URI.</summary>
    public string SchemaUri { get; }

    /// <summary>Initializes the exception.</summary>
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
