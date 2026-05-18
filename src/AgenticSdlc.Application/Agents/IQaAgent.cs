// AgenticSdlc.Application/Agents/IQaAgent.cs
// Phase 3 — Contract cho QA Agent.

using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Domain.Code;
using AgenticSdlc.Domain.Qa;
using AgenticSdlc.Domain.Requirements;
using AgenticSdlc.Domain.Testing;

namespace AgenticSdlc.Application.Agents;

/// <summary>
/// Đánh giá nhất quán requirement-code-test. Trả <see cref="QaReport"/> với
/// cờ <see cref="QaReport.IterationNeeded"/> drive vòng lặp.
/// </summary>
public interface IQaAgent
{
    /// <summary>Chạy agent.</summary>
    Task<QaReport> RunAsync(
        RequirementSpec spec,
        CodeArtifact code,
        TestArtifact tests,
        CancellationToken cancellationToken = default);
}
