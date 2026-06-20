using System;
using System.Data;
using System.Globalization;
using Dapper;

namespace AgentOs.SharedKernel.Persistence;

/// <summary>
/// One-time Dapper configuration for the read-side. Npgsql maps a Postgres <c>timestamptz</c> column to
/// CLR <see cref="DateTime"/> (UTC) by default, so Dapper cannot bind it to a record/constructor parameter
/// typed <see cref="DateTimeOffset"/> — materialization throws "a parameterless default constructor or one
/// matching signature … is required". This handler bridges that gap for EVERY Dapper read of a
/// <see cref="DateTimeOffset"/> across the app (Orchestration, Workspaces, Sessions, Pipeline runs, …).
/// Registered from <see cref="PersistenceServiceCollectionExtensions.AddNpgsqlConnectionFactory"/>.
/// </summary>
public static class DapperConfig
{
    private static readonly object Gate = new();
    private static bool _configured;

    /// <summary>Idempotently registers the Dapper type handlers (safe to call once per host process).</summary>
    public static void EnsureConfigured()
    {
        if (_configured) { return; }
        lock (Gate)
        {
            if (_configured) { return; }
            SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());
            _configured = true;
        }
    }

    private sealed class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        // READ: Npgsql returns timestamptz as a UTC DateTime; wrap it as a UTC DateTimeOffset.
        public override DateTimeOffset Parse(object value) => value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            string s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
            _ => throw new DataException($"Cannot convert {value?.GetType().Name ?? "null"} to DateTimeOffset."),
        };

        // WRITE: hand Npgsql a UTC DateTime so a timestamptz parameter binds regardless of the source offset.
        public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
            => parameter.Value = value.UtcDateTime;
    }
}
