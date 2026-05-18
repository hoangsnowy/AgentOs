// AgenticSdlc.Application/Agents/ITestingAgent.cs
// Phase 3 — Contract cho Testing Agent.

using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Domain.Code;
using AgenticSdlc.Domain.Qa;
using AgenticSdlc.Domain.Requirements;
using AgenticSdlc.Domain.Testing;

namespace AgenticSdlc.Application.Agents;

/// <summary>
/// Sinh xUnit test (happy / edge / error) từ <see cref="RequirementSpec"/> + <see cref="CodeArtifact"/>.
/// </summary>
public interface ITestingAgent
{
    /// <summary>Chạy agent.</summary>
    Task<TestArtifact> RunAsync(
        RequirementSpec spec,
        CodeArtifact code,
        QaReport? previousFeedback = null,
        CancellationToken cancellationToken = default);
}
