// M1 — durable IToolInvocationLog. The gateway that writes evidence is a singleton, so this resolves a
// per-operation DI scope (like EfAppConfigStore) to reach the scoped ToolsDbContext. Appends are
// best-effort: a persistence failure must never break the tool call, so it is swallowed + logged.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Tools;
using AgentOs.Modules.Tools.Persistence;
using AgentOs.Modules.Tools.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentOs.Modules.Tools.Evidence;

internal sealed class EfToolInvocationLog : IToolInvocationLog
{
    private readonly IServiceProvider _rootProvider;
    private readonly ILogger<EfToolInvocationLog> _logger;

    public EfToolInvocationLog(IServiceProvider rootProvider, ILogger<EfToolInvocationLog> logger)
    {
        _rootProvider = rootProvider;
        _logger = logger;
    }

    public async Task AppendAsync(ToolInvocationEvidence entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        try
        {
            await using var scope = _rootProvider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ToolsDbContext>();
            db.ToolInvocations.Add(new ToolInvocationEvidenceEntity
            {
                Id = Guid.NewGuid(),
                CallId = entry.CallId,
                ToolName = entry.ToolName,
                TenantId = entry.TenantId,
                RunId = entry.RunId,
                SessionId = entry.SessionId,
                Input = entry.Input,
                Output = entry.Output,
                IsError = entry.IsError,
                StartedUtc = entry.StartedUtc,
                FinishedUtc = entry.FinishedUtc,
            });
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Evidence is best-effort — never break the tool call on a persistence failure.
            _logger.LogWarning(ex, "Failed to persist tool-invocation evidence for {ToolName}", entry.ToolName);
        }
    }

    public async Task<IReadOnlyList<ToolInvocationEvidence>> ListRecentAsync(
        string tenantId, int limit = 50, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return Array.Empty<ToolInvocationEvidence>();
        }

        await using var scope = _rootProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ToolsDbContext>();
        var rows = await db.ToolInvocations
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.StartedUtc)
            .Take(Math.Max(1, limit))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(x => new ToolInvocationEvidence(
            x.CallId, x.ToolName, x.TenantId, x.RunId, x.Input, x.Output, x.IsError,
            x.StartedUtc, x.FinishedUtc, x.SessionId)).ToList();
    }
}
