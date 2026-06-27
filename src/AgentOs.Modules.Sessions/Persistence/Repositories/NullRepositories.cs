// No-op fallbacks so the host boots stateless when no ConnectionStrings:DefaultConnection is set
// (CI / local probes). Mirrors NullWorkspaceRepository. With no DB, no runner can be paired and no
// session persisted — the remote runner is simply inert rather than crashing the host.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Sessions;
using AgentOs.Modules.Sessions.Persistence.Entities;
using AgentOs.SharedKernel.Persistence;

namespace AgentOs.Modules.Sessions.Persistence.Repositories;

internal sealed class NullRunnerRepository : IRunnerRepository
{
    public Task<IReadOnlyList<RunnerEntity>> ListAsync(int limit = Page.DefaultLimit, int offset = 0, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RunnerEntity>>(Array.Empty<RunnerEntity>());

    public Task<RunnerEntity?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult<RunnerEntity?>(null);

    public Task AddAsync(RunnerEntity runner, CancellationToken ct = default) => Task.CompletedTask;

    public Task<bool> SetStatusAsync(Guid id, string status, CancellationToken ct = default) =>
        Task.FromResult(false);

    public Task<IReadOnlyList<RunnerEntity>> ListForTenantAsync(string tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RunnerEntity>>(Array.Empty<RunnerEntity>());

    public Task AddForTenantAsync(RunnerEntity runner, CancellationToken ct = default) => Task.CompletedTask;

    public Task<bool> SetStatusForTenantAsync(string tenantId, Guid id, string status, CancellationToken ct = default) =>
        Task.FromResult(false);
}

internal sealed class NullSessionRepository : ISessionRepository
{
    public Task<IReadOnlyList<RemoteSessionEntity>> ListAsync(int limit = Page.DefaultLimit, int offset = 0, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RemoteSessionEntity>>(Array.Empty<RemoteSessionEntity>());

    public Task<RemoteSessionEntity?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult<RemoteSessionEntity?>(null);

    public Task AddAsync(RemoteSessionEntity session, CancellationToken ct = default) => Task.CompletedTask;

    public Task<bool> CloseAsync(Guid id, DateTimeOffset closedAtUtc, CancellationToken ct = default) =>
        Task.FromResult(false);

    public Task<IReadOnlyList<RemoteSessionEntity>> ListForTenantAsync(string tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RemoteSessionEntity>>(Array.Empty<RemoteSessionEntity>());

    public Task AddForTenantAsync(RemoteSessionEntity session, CancellationToken ct = default) => Task.CompletedTask;

    public Task<bool> CloseForTenantAsync(string tenantId, Guid id, DateTimeOffset closedAtUtc, CancellationToken ct = default) =>
        Task.FromResult(false);

    public Task<bool> UpdateRunResultAsync(
        string tenantId, Guid id, string status, string? prUrl, string? errorMessage,
        CancellationToken ct = default) =>
        Task.FromResult(false);

    public Task<IReadOnlyList<SessionRepoEntity>> ListReposForTenantAsync(string tenantId, Guid sessionId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SessionRepoEntity>>(Array.Empty<SessionRepoEntity>());

    public Task<IReadOnlyList<SessionRepoEntity>> ListAllReposForTenantAsync(string tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SessionRepoEntity>>(Array.Empty<SessionRepoEntity>());

    public Task AddRepoForTenantAsync(SessionRepoEntity repo, CancellationToken ct = default) => Task.CompletedTask;

    public Task<bool> UpdateRepoRunResultAsync(
        string tenantId, Guid sessionRepoId, string status, string? branchName, string? prUrl, string? errorMessage,
        CancellationToken ct = default) =>
        Task.FromResult(false);

    public Task<bool> RecomputeSessionStatusForTenantAsync(string tenantId, Guid sessionId, CancellationToken ct = default) =>
        Task.FromResult(false);
}

internal sealed class NullRunnerDirectory : IRunnerDirectory
{
    public Task<RunnerIdentity?> FindForPairingAsync(Guid runnerId, CancellationToken cancellationToken = default) =>
        Task.FromResult<RunnerIdentity?>(null);
}
