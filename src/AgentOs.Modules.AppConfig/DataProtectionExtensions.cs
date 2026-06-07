// Shared DataProtection wiring for the Api + Web hosts. Replaces a bare AddDataProtection() (which
// keeps the key ring in memory → tenant secrets + auth cookies break on every restart/scale, and the
// two hosts can't decrypt each other's data). When a Postgres connection string is configured the key
// ring is persisted to `config.data_protection_keys` and SHARED via a fixed application name; with no
// DB (CI / standalone dev) it degrades to an ephemeral in-memory ring so the host still boots.

using AgentOs.Modules.AppConfig.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AgentOs.Modules.AppConfig;

/// <summary>Host extension: durable, cross-host DataProtection key ring.</summary>
public static class DataProtectionExtensions
{
    /// <summary>The shared application name both hosts protect/unprotect under — must match so a value
    /// encrypted by the Web (e.g. a tenant LLM key saved in Settings) is decryptable by the Api.</summary>
    private const string SharedApplicationName = "AgentOS";

    /// <summary>Adds DataProtection, persisting the key ring to Postgres when configured.</summary>
    public static IHostApplicationBuilder AddAgentOsDataProtection(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
        var dataProtection = builder.Services.AddDataProtection().SetApplicationName(SharedApplicationName);

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            builder.Services.AddDbContext<DataProtectionDbContext>(opt =>
                opt.UseNpgsql(connectionString, npg =>
                    npg.MigrationsHistoryTable("__DataProtectionMigrationsHistory", schema: "config")));

            dataProtection.PersistKeysToDbContext<DataProtectionDbContext>();
        }

        return builder;
    }
}
