// EF-backed IRunnerDirectory. Used by the SignalR hub during the pairing handshake, where there is no
// authenticated tenant yet — so the lookup bypasses the tenant query filter (IgnoreQueryFilters) and
// finds the runner by its unguessable id. The caller MUST then verify the presented token against the
// returned TokenHash before trusting TenantId / OwnerUserId. The id alone grants nothing.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace AgentOs.Modules.Sessions.Persistence.Repositories;

internal sealed class EfRunnerDirectory : IRunnerDirectory
{
    private readonly SessionsDbContext _db;

    public EfRunnerDirectory(SessionsDbContext db) => _db = db;

    public async Task<RunnerIdentity?> FindForPairingAsync(Guid runnerId, CancellationToken cancellationToken = default)
    {
        return await _db.Runners
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => r.Id == runnerId)
            .Select(r => new RunnerIdentity(r.Id, r.TenantId, r.OwnerUserId, r.TokenHash, r.Status))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
