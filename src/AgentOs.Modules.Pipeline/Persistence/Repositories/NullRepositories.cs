// No-op repos when NO connection string is configured (CI/local without a DB → app runs stateless).
using AgentOs.Modules.Pipeline.Persistence;

namespace AgentOs.Modules.Pipeline.Persistence.Repositories;

internal sealed class NullPipelineRunRepository : IPipelineRunRepository
{
    public Task SaveAsync(PipelineRunRecord record, CancellationToken ct = default) => Task.CompletedTask;

    public Task<PipelineRunRecord?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult<PipelineRunRecord?>(null);

    public Task<IReadOnlyList<PipelineRunSummary>> ListAsync(int limit = 50, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PipelineRunSummary>>([]);

    public Task<CostSummary> GetCostSummaryForTenantAsync(
        string tenantId, DateTimeOffset? since = null, CancellationToken ct = default) =>
        Task.FromResult(CostSummary.Empty);
}

internal sealed class NullOrchestrationRepository : IOrchestrationRepository
{
    public Task<IReadOnlyList<OrchestrationRecord>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<OrchestrationRecord>>([]);

    public Task<OrchestrationRecord?> GetAsync(string id, CancellationToken ct = default) =>
        Task.FromResult<OrchestrationRecord?>(null);

    public Task UpsertAsync(OrchestrationRecord record, CancellationToken ct = default) => Task.CompletedTask;

    public Task DeleteAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
}
