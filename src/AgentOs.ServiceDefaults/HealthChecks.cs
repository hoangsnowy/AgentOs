// Readiness health checks shared by the API + Web hosts. Registered conditionally by
// AddDefaultHealthChecks: Postgres only when a connection string is configured, Keycloak only
// outside Development (standalone/dev runs must stay healthy with no external services).
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace AgentOs.ServiceDefaults;

/// <summary>Readiness probe for the Postgres database behind <c>ConnectionStrings:DefaultConnection</c>.</summary>
internal sealed class PostgresHealthCheck(string connectionString) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            _ = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy("Postgres reachable.");
        }
        catch (NpgsqlException ex)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Postgres unreachable.", ex);
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Postgres unreachable.", ex);
        }
        catch (InvalidOperationException ex)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Postgres connection string invalid.", ex);
        }
    }
}

/// <summary>Readiness probe for the OIDC identity provider: fetches the discovery document
/// (<c>/.well-known/openid-configuration</c>) under the configured authority.</summary>
internal sealed class OidcMetadataHealthCheck(IHttpClientFactory httpClientFactory, Uri metadataUri) : IHealthCheck
{
    /// <summary>Named HttpClient used for the probe (no auth, short timeout).</summary>
    internal const string ClientName = "agentos-health";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            using var client = httpClientFactory.CreateClient(ClientName);
            client.Timeout = TimeSpan.FromSeconds(5);
            using var response = await client.GetAsync(metadataUri, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("Identity provider metadata reachable.")
                : new HealthCheckResult(
                    context.Registration.FailureStatus,
                    $"Identity provider returned HTTP {(int)response.StatusCode}.");
        }
        catch (HttpRequestException ex)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Identity provider unreachable.", ex);
        }
        catch (TaskCanceledException ex)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Identity provider probe timed out.", ex);
        }
    }
}

/// <summary>Writes a health report as JSON ({ status, entries: { name: { status, description } } })
/// so orchestrators and humans see WHICH dependency failed, not just a bare "Unhealthy" string.
/// Descriptions are check-authored constants; exception details are never serialized.</summary>
internal static class HealthJsonWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        context.Response.ContentType = "application/json; charset=utf-8";
        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = (long)report.TotalDuration.TotalMilliseconds,
            entries = report.Entries.ToDictionary(
                e => e.Key,
                e => new { status = e.Value.Status.ToString(), description = e.Value.Description }),
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
