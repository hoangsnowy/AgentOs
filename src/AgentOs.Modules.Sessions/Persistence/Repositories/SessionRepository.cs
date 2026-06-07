// EF-backed session repository. The DbContext global query filter enforces tenant isolation on reads;
// AddAsync stamps TenantId from ITenantContext so writes can't escape the tenant.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Modules.Sessions.Persistence.Entities;
using AgentOs.SharedKernel.Identity;
using AgentOs.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentOs.Modules.Sessions.Persistence.Repositories;

internal sealed class SessionRepository : ISessionRepository
{
    private readonly SessionsDbContext _db;
    private readonly ITenantContext _tenant;

    public SessionRepository(SessionsDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<RemoteSessionEntity>> ListAsync(int limit = Page.DefaultLimit, int offset = 0, CancellationToken ct = default)
    {
        return await _db.Sessions
            .AsNoTracking()
            .OrderByDescending(s => s.CreatedAtUtc)
            .Skip(Page.ClampOffset(offset))
            .Take(Page.ClampLimit(limit))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<RemoteSessionEntity?> GetAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Sessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(RemoteSessionEntity session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.TenantId = _tenant.TenantId;
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> CloseAsync(Guid id, DateTimeOffset closedAtUtc, CancellationToken ct = default)
    {
        var row = await _db.Sessions.FirstOrDefaultAsync(s => s.Id == id, ct).ConfigureAwait(false);
        if (row is null)
        {
            return false;
        }
        row.Status = "Closed";
        row.ClosedAtUtc = closedAtUtc;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task<IReadOnlyList<RemoteSessionEntity>> ListForTenantAsync(string tenantId, CancellationToken ct = default)
    {
        return await _db.Sessions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task AddForTenantAsync(RemoteSessionEntity session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> CloseForTenantAsync(string tenantId, Guid id, DateTimeOffset closedAtUtc, CancellationToken ct = default)
    {
        var row = await _db.Sessions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId, ct)
            .ConfigureAwait(false);
        if (row is null)
        {
            return false;
        }
        row.Status = "Closed";
        row.ClosedAtUtc = closedAtUtc;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> UpdateRunResultAsync(
        string tenantId, Guid id, string status, string? prUrl, string? errorMessage,
        CancellationToken ct = default)
    {
        var row = await _db.Sessions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId, ct)
            .ConfigureAwait(false);
        if (row is null)
        {
            return false;
        }
        row.Status = status;
        row.PrUrl = prUrl;
        row.Error = errorMessage;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    // ── Multi-repo ───────────────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<SessionRepoEntity>> ListReposForTenantAsync(string tenantId, Guid sessionId, CancellationToken ct = default)
    {
        return await _db.SessionRepos
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.SessionId == sessionId)
            .OrderBy(r => r.Owner).ThenBy(r => r.Repo)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SessionRepoEntity>> ListAllReposForTenantAsync(string tenantId, CancellationToken ct = default)
    {
        return await _db.SessionRepos
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task AddRepoForTenantAsync(SessionRepoEntity repo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(repo);
        _db.SessionRepos.Add(repo);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> UpdateRepoRunResultAsync(
        string tenantId, Guid sessionRepoId, string status, string? branchName, string? prUrl, string? errorMessage,
        CancellationToken ct = default)
    {
        var row = await _db.SessionRepos
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == sessionRepoId && r.TenantId == tenantId, ct)
            .ConfigureAwait(false);
        if (row is null)
        {
            return false;
        }
        row.Status = status;
        row.BranchName = branchName;
        row.PrUrl = prUrl;
        row.Error = errorMessage;
        if (status is "Done" or "Failed")
        {
            row.CompletedAtUtc = DateTimeOffset.UtcNow;
        }
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> RecomputeSessionStatusForTenantAsync(string tenantId, Guid sessionId, CancellationToken ct = default)
    {
        var session = await _db.Sessions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.TenantId == tenantId, ct)
            .ConfigureAwait(false);
        if (session is null)
        {
            return false;
        }

        var repos = await _db.SessionRepos
            .IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && r.SessionId == sessionId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (repos.Count == 0)
        {
            return false;
        }

        session.Status = repos.Any(r => r.Status == "Failed") ? "Failed"
            : repos.All(r => r.Status == "Done") ? "Done"
            : "Running";

        // Mirror the first PR + first error onto the parent for back-compat display.
        session.PrUrl = repos.FirstOrDefault(r => !string.IsNullOrEmpty(r.PrUrl))?.PrUrl;
        session.Error = repos.FirstOrDefault(r => !string.IsNullOrEmpty(r.Error))?.Error;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }
}
