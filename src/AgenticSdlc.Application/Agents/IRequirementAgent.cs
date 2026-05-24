// AgenticSdlc.Application/Agents/IRequirementAgent.cs
// Phase 3 — Contract for the Requirement Agent (thesis Section 2.4.1).

using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Domain.Pipeline;
using AgenticSdlc.Domain.Requirements;

namespace AgenticSdlc.Application.Agents;

/// <summary>
/// Analyzes a user story → structured <see cref="RequirementSpec"/>.
/// </summary>
public interface IRequirementAgent
{
    /// <summary>Runs the agent.</summary>
    /// <param name="story">The input user story (already validated).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<RequirementSpec> RunAsync(UserStory story, CancellationToken cancellationToken = default);
}
