// EF Core impl: saves PipelineResult (jsonb) + RunMetric rows, reads back + lists summaries.
using System.Text.Json;
using AgenticSdlc.Application.Metrics;
using AgenticSdlc.Application.Persistence;
using AgenticSdlc.Domain.Pipeline;
using AgenticSdlc.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgenticSdlc.Infrastructure.Persistence.Repositories;

internal sealed class PipelineRunRepository(AgenticSdlcDbContext db) : IPipelineRunRepository
{
    public async Task SaveAsync(PipelineRunRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        var result = record.Result;

        var entity = new PipelineRunEntity
        {
            Id = record.Id,
            UserStoryText = result.UserStory.Description,
            Status = result.Status.ToString(),
            TotalCostUsd = result.TotalMetrics.CostUsd,
            TotalTokensIn = result.TotalMetrics.InputTokens,
            TotalTokensOut = result.TotalMetrics.OutputTokens,
            IterationCount = result.IterationCount,
            CreatedAtUtc = record.CreatedAtUtc,
            CompletedAtUtc = record.CompletedAtUtc,
            ResultJson = JsonSerializer.Serialize(result, PersistenceJson.Options),
        };

        foreach (var m in record.Metrics)
        {
            entity.Metrics.Add(new RunMetricEntity
            {
                RunId = record.Id,
                KcId = m.KcId,
                Iteration = m.Iteration,
                AgentName = m.AgentName,
                Model = m.Model,
                Provider = m.Provider,
                TokensIn = m.TokensIn,
                TokensOut = m.TokensOut,
                LatencyMs = m.LatencyMs,
                CostUsd = m.CostUsd,
                Success = m.Success,
                ErrorMessage = m.ErrorMessage,
                TimestampUtc = m.Timestamp,
            });
        }

        db.PipelineRuns.Add(entity);
        await db.SaveChangesAsync(ct);
    }

    public async Task<PipelineRunRecord?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await db.PipelineRuns
            .Include(x => x.Metrics)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null)
        {
            return null;
        }

        var result = JsonSerializer.Deserialize<PipelineResult>(entity.ResultJson, PersistenceJson.Options);
        if (result is null)
        {
            return null;
        }

        var metrics = entity.Metrics
            .OrderBy(m => m.Id)
            .Select(m => new RunMetric(
                entity.Id.ToString(),
                m.KcId,
                m.Iteration,
                m.AgentName,
                m.Model,
                m.Provider,
                m.TokensIn,
                m.TokensOut,
                m.LatencyMs,
                m.CostUsd,
                m.Success,
                m.ErrorMessage,
                m.TimestampUtc))
            .ToList();

        return new PipelineRunRecord(
            entity.Id,
            result,
            metrics,
            entity.CreatedAtUtc,
            entity.CompletedAtUtc ?? entity.CreatedAtUtc);
    }

    public async Task<IReadOnlyList<PipelineRunSummary>> ListAsync(int limit = 50, CancellationToken ct = default)
    {
        return await db.PipelineRuns
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(limit)
            .Select(x => new PipelineRunSummary(
                x.Id,
                x.Status,
                x.TotalCostUsd,
                x.IterationCount,
                x.CreatedAtUtc,
                x.CompletedAtUtc,
                x.UserStoryText.Length > 120 ? x.UserStoryText.Substring(0, 120) : x.UserStoryText))
            .ToListAsync(ct);
    }
}
