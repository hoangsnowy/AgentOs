// AgenticSdlc.Domain — Placeholder file để project compile được ở Phase 1.
// Sẽ được thay bằng các domain model thật ở Phase 3:
//   - RequirementSpec, CodeArtifact, TestArtifact, QaReport,
//     Verdict, PipelineResult, QaFeedback, ...

namespace AgenticSdlc.Domain;

/// <summary>
/// Marker để xác nhận assembly Domain đã được tham chiếu thành công.
/// Sẽ bị xoá trong Phase 3 khi thêm domain entity thật.
/// </summary>
internal static class AssemblyMarker
{
    public const string Name = "AgenticSdlc.Domain";
}
