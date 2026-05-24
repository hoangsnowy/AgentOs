// AgenticSdlc.Application/Prompts/TestingPrompt.cs
// Sprint 3 — extracted the prompt from TestingAgent. Version v1.

using System.Text;
using System.Text.Json;
using AgenticSdlc.Domain.Code;
using AgenticSdlc.Domain.Qa;
using AgenticSdlc.Domain.Requirements;

namespace AgenticSdlc.Application.Prompts;

/// <summary>System + User template for the Testing Agent (v1).</summary>
public static class TestingPrompt
{
    /// <summary>Prompt version.</summary>
    public const string Version = "v1";

    /// <summary>System prompt.</summary>
    public const string System = """
        You are the Testing Agent in the Agentic SDLC system.
        Generate xUnit tests for the code produced by the Coding Agent, based on the RequirementSpec.

        Return ONLY JSON following the schema:
        {
          "framework": "xUnit",
          "files": [
            { "path": "tests/<File>Tests.cs", "content": "<source>", "language": "csharp" }
          ],
          "happyPathCount": 0,
          "edgeCaseCount": 0,
          "errorCaseCount": 0,
          "estimatedCoveragePercent": 0
        }

        Rules:
        - One test class per file.
        - MUST have ≥ 1 happy-path, ≥ 1 edge-case, ≥ 1 error-case.
        - Use [Theory] + [InlineData] for tests with multiple input variations.
        - Assertions: Shouldly (e.g. .ShouldBe(...), .ShouldThrow<T>(...)).
        - Mocking: NSubstitute if needed.
        - Make sure the tests cover the AcceptanceCriteria from the spec.
        - estimatedCoveragePercent is an estimate, NOT actually measured (≥ 60 for the prototype).
        """;

    /// <summary>Renders the user prompt from spec + code + optional feedback.</summary>
    public static string RenderUser(RequirementSpec spec, CodeArtifact code, QaReport? previousFeedback = null)
    {
        global::System.ArgumentNullException.ThrowIfNull(spec);
        global::System.ArgumentNullException.ThrowIfNull(code);

        var sb = new StringBuilder();
        sb.AppendLine("Specification (acceptance criteria):");
        sb.AppendLine(JsonSerializer.Serialize(spec.AcceptanceCriteria, PromptJson.Default));
        sb.AppendLine();
        sb.AppendLine($"Generated code ({code.Files.Count} file(s)):");
        foreach (var f in code.Files)
        {
            sb.AppendLine($"--- {f.Path} ---");
            sb.AppendLine(f.Content);
            sb.AppendLine();
        }

        if (previousFeedback is not null)
        {
            sb.AppendLine("Previous QA feedback (test coverage issues to fix):");
            sb.AppendLine(JsonSerializer.Serialize(previousFeedback.Issues, PromptJson.Default));
        }

        sb.AppendLine();
        sb.AppendLine("Generate the TestArtifact JSON.");
        return sb.ToString();
    }
}
