// AgenticSdlc.Application/Agents/IRequirementAgent.cs
// Phase 3 — Contract cho Requirement Agent (Mục 2.4.1 luận văn).

using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Domain.Pipeline;
using AgenticSdlc.Domain.Requirements;

namespace AgenticSdlc.Application.Agents;

/// <summary>
/// Phân tích user story → <see cref="RequirementSpec"/> structured.
/// </summary>
public interface IRequirementAgent
{
    /// <summary>Chạy agent.</summary>
    /// <param name="story">User story đầu vào (đã validate).</param>
    /// <param name="cancellationToken">Token huỷ.</param>
    Task<RequirementSpec> RunAsync(UserStory story, CancellationToken cancellationToken = default);
}
