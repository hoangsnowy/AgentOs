// Tenant-isolation tests for the read/write-through RuntimeOverrides impl. Wires the impl over
// a tenant-aware in-memory IAppConfigStore + a switchable ITenantContext so we can flip tenants
// between calls and prove the override values move with them.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Modules.AppConfig;
using AgentOs.Modules.Llm;
using AgentOs.SharedKernel.Identity;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Llm;

public sealed class RuntimeOverridesTests
{
    private static readonly string[] ThreeKeys = { "k1", "k2", "k3" };

    [Fact]
    public void AnthropicApiKey_TenantAWrites_TenantBReadsNull()
    {
        var ctx = new SwitchableTenantContext("acme");
        var store = new TenantScopedInMemoryStore(ctx);
        var sp = BuildProvider(ctx, store);
        var overrides = new RuntimeOverrides(sp);

        overrides.AnthropicApiKey = "sk-acme-1";
        overrides.AnthropicApiKey.ShouldBe("sk-acme-1");

        ctx.TenantId = "globex";
        overrides.AnthropicApiKey.ShouldBeNull();

        ctx.TenantId = "acme";
        overrides.AnthropicApiKey.ShouldBe("sk-acme-1");
    }

    [Fact]
    public void GitHubPat_PerTenantIsolated()
    {
        var ctx = new SwitchableTenantContext("acme");
        var store = new TenantScopedInMemoryStore(ctx);
        var sp = BuildProvider(ctx, store);
        var overrides = new RuntimeOverrides(sp);

        overrides.GitHubPat = "ghp_acme";
        ctx.TenantId = "globex";
        overrides.GitHubPat = "ghp_globex";

        ctx.TenantId = "acme";
        overrides.GitHubPat.ShouldBe("ghp_acme");
        ctx.TenantId = "globex";
        overrides.GitHubPat.ShouldBe("ghp_globex");
    }

    [Fact]
    public void AnthropicApiKeys_NewlineCommaSeparated_RoundTrips()
    {
        var ctx = new SwitchableTenantContext("acme");
        var store = new TenantScopedInMemoryStore(ctx);
        var overrides = new RuntimeOverrides(BuildProvider(ctx, store));

        overrides.AnthropicApiKeys = ThreeKeys;
        overrides.AnthropicApiKeys.ShouldBe(ThreeKeys);
    }

    [Fact]
    public void Setter_WithBlank_DeletesKey()
    {
        var ctx = new SwitchableTenantContext("acme");
        var store = new TenantScopedInMemoryStore(ctx);
        var overrides = new RuntimeOverrides(BuildProvider(ctx, store));

        overrides.GitHubBaseUrl = "https://github.acme.com/api/v3";
        overrides.GitHubBaseUrl.ShouldBe("https://github.acme.com/api/v3");
        overrides.GitHubBaseUrl = null;
        overrides.GitHubBaseUrl.ShouldBeNull();
        overrides.GitHubBaseUrl = "   ";
        overrides.GitHubBaseUrl.ShouldBeNull();
    }

    [Fact]
    public void NoAppConfigStore_GettersReturnNullSettersNoOp()
    {
        // Standalone hosts may boot without persistence — RuntimeOverrides must degrade silently.
        var services = new ServiceCollection();
        var overrides = new RuntimeOverrides(services.BuildServiceProvider());
        overrides.AnthropicApiKey.ShouldBeNull();
        overrides.AnthropicApiKey = "should-be-ignored";
        overrides.AnthropicApiKey.ShouldBeNull();
    }

    private static IServiceProvider BuildProvider(SwitchableTenantContext ctx, IAppConfigStore store)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext>(ctx);
        services.AddSingleton(store);
        return services.BuildServiceProvider();
    }

    private sealed class SwitchableTenantContext : ITenantContext
    {
        public SwitchableTenantContext(string tenantId) { TenantId = tenantId; }
        public string TenantId { get; set; }
        public string? UserId => null;
        public string? UserName => null;
        public IReadOnlyList<string> Roles => Array.Empty<string>();
        public bool IsAuthenticated => true;
        public bool IsAdmin => true;
    }

    private sealed class TenantScopedInMemoryStore : IAppConfigStore
    {
        private readonly ITenantContext _ctx;
        private readonly ConcurrentDictionary<string, string> _data = new(StringComparer.Ordinal);

        public TenantScopedInMemoryStore(ITenantContext ctx) { _ctx = ctx; }

        private string Key(string key) => $"{_ctx.TenantId}::{key}";

        public ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_data.TryGetValue(Key(key), out var v) ? v : null);

        public ValueTask SetAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            _data[Key(key)] = value;
            return ValueTask.CompletedTask;
        }

        public ValueTask DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            _data.TryRemove(Key(key), out _);
            return ValueTask.CompletedTask;
        }

        public ValueTask<string?> GetForTenantAsync(string tenantId, string key, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_data.TryGetValue($"{tenantId}::{key}", out var v) ? v : null);

        public ValueTask SetForTenantAsync(string tenantId, string key, string value, CancellationToken cancellationToken = default)
        {
            _data[$"{tenantId}::{key}"] = value;
            return ValueTask.CompletedTask;
        }

        public ValueTask DeleteForTenantAsync(string tenantId, string key, CancellationToken cancellationToken = default)
        {
            _data.TryRemove($"{tenantId}::{key}", out _);
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken cancellationToken = default)
        {
            var tenantPrefix = $"{_ctx.TenantId}::{prefix}";
            return ValueTask.FromResult<IReadOnlyList<string>>(
                _data.Keys.Where(k => k.StartsWith(tenantPrefix, StringComparison.Ordinal))
                    .Select(k => k[(_ctx.TenantId.Length + 2)..])
                    .OrderBy(k => k)
                    .ToList());
        }
    }
}
