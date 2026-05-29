// Runtime-mutable configuration row. Value is encrypted at rest via ASP.NET DataProtection before
// it is written; EfAppConfigStore decrypts on read. Primary key is (TenantId, Key) so each tenant
// owns its own values (per-tenant LLM keys, per-tenant GitHub PAT, …); pre-tenant rows live under
// ITenantContext.DefaultTenantId.

using System;
using AgenticSdlc.SharedKernel.Identity;

namespace AgenticSdlc.Modules.AppConfig.Persistence.Entities;

/// <summary>One key/value setting. Value is ciphertext.</summary>
public sealed class AppConfigEntity
{
    /// <summary>Owning tenant — composite primary key alongside <see cref="Key"/>.</summary>
    public string TenantId { get; set; } = ITenantContext.DefaultTenantId;

    /// <summary>Configuration key, e.g. <c>Llm:Claude:ApiKey</c>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>DataProtection-encrypted value (base64 ciphertext).</summary>
    public string EncryptedValue { get; set; } = string.Empty;

    /// <summary>Last write timestamp (UTC).</summary>
    public DateTime UpdatedAtUtc { get; set; }
}
