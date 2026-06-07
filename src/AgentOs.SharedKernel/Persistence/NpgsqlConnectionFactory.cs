using System;
using System.Data.Common;
using Npgsql;

namespace AgentOs.SharedKernel.Persistence;

/// <inheritdoc />
public sealed class NpgsqlConnectionFactory : INpgsqlConnectionFactory
{
    private readonly string _connectionString;

    public NpgsqlConnectionFactory(string connectionString)
        => _connectionString = string.IsNullOrWhiteSpace(connectionString)
            ? throw new ArgumentException("connection string required", nameof(connectionString))
            : connectionString;

    public DbConnection Create() => new NpgsqlConnection(_connectionString);
}
