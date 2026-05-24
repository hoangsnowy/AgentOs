// AgenticSdlc.Application/Validation/SchemaNames.cs
// Sprint 3 — canonical schema names (match the logical name used at registration).

namespace AgenticSdlc.Application.Validation;

/// <summary>Schema name constants registered with <see cref="ILlmOutputValidator"/>.</summary>
public static class SchemaNames
{
    /// <summary>RequirementSpec v1.</summary>
    public const string RequirementSpecV1 = "requirement-spec.v1";

    /// <summary>CodeArtifact v1.</summary>
    public const string CodeArtifactV1 = "code-artifact.v1";

    /// <summary>TestArtifact v1.</summary>
    public const string TestArtifactV1 = "test-artifact.v1";
}
