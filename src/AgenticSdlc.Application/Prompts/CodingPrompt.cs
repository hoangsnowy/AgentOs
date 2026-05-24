// AgenticSdlc.Application/Prompts/CodingPrompt.cs
// Sprint 3 — extracted the prompt from CodingAgent. Version v1.

using System.Text;
using System.Text.Json;
using AgenticSdlc.Domain.Qa;
using AgenticSdlc.Domain.Requirements;

namespace AgenticSdlc.Application.Prompts;

/// <summary>System + User template for the Coding Agent (v1).</summary>
public static class CodingPrompt
{
    /// <summary>Prompt version.</summary>
    public const string Version = "v1";

    /// <summary>System prompt.</summary>
    public const string System = """
        You are the Coding Agent in the Agentic SDLC system.
        Generate C# source code (.NET 10) following Clean Architecture for the specification the user provides.

        Return ONLY JSON following the schema:
        {
          "projectName": "PascalCase",
          "architecture": "Clean Architecture",
          "files": [
            { "path": "src/<Layer>/<File>.cs", "content": "<source code>", "language": "csharp" }
          ],
          "notes": "Assumption / TODO notes (in English)"
        }

        Rules:
        - MUST have ≥ 1 entity class in the Domain layer.
        - MUST have ≥ 1 controller or minimal API endpoint in the Api layer.
        - The code must compile with .NET 10 (nullable enable, file-scoped namespace).
        - Use forward slashes in paths.
        - If previousFeedback is present: prioritize fixing every Critical/Major severity issue in the feedback.
        - No markdown fence around the JSON, no prose before/after.
        """;

    /// <summary>Renders the user prompt from the spec + optional QA feedback.</summary>
    public static string RenderUser(RequirementSpec spec, QaReport? previousFeedback = null)
    {
        global::System.ArgumentNullException.ThrowIfNull(spec);

        var sb = new StringBuilder();
        sb.AppendLine("Specification (JSON):");
        sb.AppendLine(JsonSerializer.Serialize(new
        {
            spec.Title,
            spec.Summary,
            spec.Entities,
            spec.Endpoints,
            spec.AcceptanceCriteria,
        }, PromptJson.Default));

        if (previousFeedback is not null)
        {
            sb.AppendLine();
            sb.AppendLine("Previous QA feedback (must be fixed this time):");
            sb.AppendLine(JsonSerializer.Serialize(new
            {
                previousFeedback.Score,
                previousFeedback.Issues,
                previousFeedback.Recommendations,
            }, PromptJson.Default));
        }

        sb.AppendLine();
        sb.AppendLine("Generate the CodeArtifact JSON.");
        return sb.ToString();
    }
}
