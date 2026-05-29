// AgenticSdlc.Application/Agents/IQaAgent.cs
// Phase 3 — Contract for the QA Agent.

using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Domain.Code;
using AgenticSdlc.Domain.Qa;
using AgenticSdlc.Domain.Requirements;
using AgenticSdlc.Domain.Testing;

namespace AgenticSdlc.Modules.Pipeline.Agents;

/// <summary>
/// Evaluates requirement-code-test consistency. Returns a <see cref="QaReport"/> whose
/// <see cref="QaReport.IterationNeeded"/> flag drives the loop.
/// </summary>
public interface IQaAgent
{
    /// <summary>Runs the agent.</summary>
    Task<QaReport> RunAsync(
        RequirementSpec spec,
        CodeArtifact code,
        TestArtifact tests,
        CancellationToken cancellationToken = default);
}
