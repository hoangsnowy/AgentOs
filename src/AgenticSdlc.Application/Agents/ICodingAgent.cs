// AgenticSdlc.Application/Agents/ICodingAgent.cs
// Phase 3 — Contract cho Coding Agent.

using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Domain.Code;
using AgenticSdlc.Domain.Qa;
using AgenticSdlc.Domain.Requirements;

namespace AgenticSdlc.Application.Agents;

/// <summary>
/// Sinh source code C# (Clean Architecture) từ <see cref="RequirementSpec"/>.
/// Có thể nhận <see cref="QaReport"/> feedback từ vòng trước để regenerate.
/// </summary>
public interface ICodingAgent
{
    /// <summary>Chạy agent.</summary>
    /// <param name="spec">Spec yêu cầu.</param>
    /// <param name="previousFeedback">QA report vòng trước — null nếu vòng đầu.</param>
    /// <param name="cancellationToken">Token huỷ.</param>
    System.Threading.Tasks.Task<CodeArtifact> RunAsync(
        RequirementSpec spec,
        QaReport? previousFeedback = null,
        CancellationToken cancellationToken = default);
}
