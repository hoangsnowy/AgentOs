// AgenticSdlc.Application/Agents/ITestingAgent.cs
// Phase 3 — Contract for the Testing Agent.

using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Domain.Code;
using AgenticSdlc.Domain.Qa;
using AgenticSdlc.Domain.Requirements;
using AgenticSdlc.Domain.Testing;

namespace AgenticSdlc.Modules.Pipeline.Agents;

/// <summary>
/// Generates xUnit tests (happy / edge / error) from a <see cref="RequirementSpec"/> + <see cref="CodeArtifact"/>.
/// </summary>
public interface ITestingAgent
{
    /// <summary>Runs the agent.</summary>
    Task<TestArtifact> RunAsync(
        RequirementSpec spec,
        CodeArtifact code,
        QaReport? previousFeedback = null,
        CancellationToken cancellationToken = default);
}
