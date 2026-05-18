// AgenticSdlc.Application/Validation/SchemaNames.cs
// Sprint 3 — tên schema chuẩn (khớp logical name khi đăng ký).

namespace AgenticSdlc.Application.Validation;

/// <summary>Constants tên schema đăng ký với <see cref="ILlmOutputValidator"/>.</summary>
public static class SchemaNames
{
    /// <summary>RequirementSpec v1.</summary>
    public const string RequirementSpecV1 = "requirement-spec.v1";

    /// <summary>CodeArtifact v1.</summary>
    public const string CodeArtifactV1 = "code-artifact.v1";

    /// <summary>TestArtifact v1.</summary>
    public const string TestArtifactV1 = "test-artifact.v1";
}
