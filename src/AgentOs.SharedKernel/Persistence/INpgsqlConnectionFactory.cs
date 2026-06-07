// The read-side seam. EF owns writes/migrations/change-tracking; hot read paths (projections,
// aggregations, pagination) run as hand-tuned Dapper SQL over a raw connection from here. Registered
// only when a connection string is configured, so repositories inject it as OPTIONAL and fall back to
// EF when it is null (CI / EF in-memory tests / no-DB boot) — mirroring the existing no-op-repo gate.

using System.Data.Common;

namespace AgentOs.SharedKernel.Persistence;

/// <summary>Creates raw ADO.NET connections to the application's Postgres database for Dapper reads.</summary>
public interface INpgsqlConnectionFactory
{
    /// <summary>Creates a new, unopened connection. The caller owns it (wrap in <c>await using</c>).</summary>
    DbConnection Create();
}
