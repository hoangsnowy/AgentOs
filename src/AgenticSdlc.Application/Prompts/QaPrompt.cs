// AgenticSdlc.Application/Prompts/QaPrompt.cs
// Sprint 3 — extracted the prompt from QaAgent. Version v1.

using System.Linq;
using System.Text;
using System.Text.Json;
using AgenticSdlc.Domain.Code;
using AgenticSdlc.Domain.Requirements;
using AgenticSdlc.Domain.Testing;

namespace AgenticSdlc.Application.Prompts;

/// <summary>System + User template for the QA Agent (v1).</summary>
public static class QaPrompt
{
    /// <summary>Prompt version.</summary>
    public const string Version = "v1";

    /// <summary>System prompt.</summary>
    public const string System = """
        You are the QA Agent in the Agentic SDLC system.
        Evaluate consistency across the 3 artifacts: RequirementSpec, CodeArtifact, TestArtifact.

        Return ONLY JSON following the schema:
        {
          "score": 0.0-1.0,
          "isConsistent": true|false,
          "iterationNeeded": true|false,
          "issues": [
            {
              "severity": "Critical|Major|Minor",
              "category": "RequirementCoverage|CodeQuality|TestCoverage|Consistency",
              "description": "English, concise",
              "location": "file path or requirement id (optional)"
            }
          ],
          "recommendations": ["Recommendation for the next regeneration iteration"]
        }

        Scoring rules:
        - score = 1.0 - (#Critical × 0.3 + #Major × 0.1 + #Minor × 0.03), clamp [0, 1].
        - isConsistent = (score ≥ 0.8) AND (#Critical == 0).
        - iterationNeeded = NOT isConsistent.
        - Every entity in the spec MUST have a corresponding code class (check by name).
        - Every endpoint in the spec MUST have a corresponding code ROUTE mapping.
        - Every acceptanceCriteria MUST be reflected in ≥ 1 test.
        """;

    /// <summary>Renders the user prompt from spec + code + tests (file excerpt trimmed to 300 chars).</summary>
    public static string RenderUser(RequirementSpec spec, CodeArtifact code, TestArtifact tests, int excerptChars = 300)
    {
        global::System.ArgumentNullException.ThrowIfNull(spec);
        global::System.ArgumentNullException.ThrowIfNull(code);
        global::System.ArgumentNullException.ThrowIfNull(tests);

        var sb = new StringBuilder();
        sb.AppendLine("Spec (entities + endpoints + acceptanceCriteria):");
        sb.AppendLine(JsonSerializer.Serialize(new { spec.Entities, spec.Endpoints, spec.AcceptanceCriteria }, PromptJson.Default));
        sb.AppendLine();
        sb.AppendLine("Code (files, abbreviated):");
        sb.AppendLine(JsonSerializer.Serialize(code.Files.Select(f => new { f.Path, Excerpt = Excerpt(f.Content, excerptChars) }), PromptJson.Default));
        sb.AppendLine();
        sb.AppendLine("Tests (files + counts):");
        sb.AppendLine(JsonSerializer.Serialize(new
        {
            tests.Framework,
            tests.HappyPathCount,
            tests.EdgeCaseCount,
            tests.ErrorCaseCount,
            tests.EstimatedCoveragePercent,
            Files = tests.Files.Select(f => new { f.Path, Excerpt = Excerpt(f.Content, excerptChars) }),
        }, PromptJson.Default));

        sb.AppendLine();
        sb.AppendLine("Generate the QaReport JSON.");
        return sb.ToString();
    }

    private static string Excerpt(string content, int maxChars)
        => string.IsNullOrEmpty(content) ? string.Empty : content.Length <= maxChars ? content : string.Concat(content.AsSpan(0, maxChars), "...");
}
