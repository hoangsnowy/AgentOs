// Persistence entity for a single pipeline run. Relational columns for querying (analytics) +
// the jsonb ResultJson column holding the full serialized PipelineResult. TenantId scopes the
// row to a Keycloak tenant; the DbContext applies a global query filter so a request only ever
// sees its own tenant's runs (see Epic D row-level isolation).
namespace AgenticSdlc.Modules.Pipeline.Persistence.Entities;

internal sealed class PipelineRunEntity
{
    public Guid Id { get; set; }

    /// <summary>Owning tenant — stamped on insert from <c>ITenantContext.TenantId</c>.</summary>
    public string TenantId { get; set; } = AgenticSdlc.SharedKernel.Identity.ITenantContext.DefaultTenantId;

    public string UserStoryText { get; set; } = string.Empty;

    /// <summary>PipelineStatus enum name: Done / MaxIterationReached / Failed.</summary>
    public string Status { get; set; } = string.Empty;

    public decimal TotalCostUsd { get; set; }

    public int TotalTokensIn { get; set; }

    public int TotalTokensOut { get; set; }

    public int IterationCount { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    /// <summary>Full PipelineResult serialized as JSON (jsonb column).</summary>
    public string ResultJson { get; set; } = string.Empty;

    public List<RunMetricEntity> Metrics { get; } = [];
}
