// AgenticSdlc.Infrastructure/Persistence/Entities/AppConfigEntity.cs
// Phase 8.4b — runtime-mutable configuration row. Value is encrypted at rest via ASP.NET
// DataProtection before it is written; EfAppConfigStore decrypts on read.

using System;

namespace AgenticSdlc.Infrastructure.Persistence.Entities;

/// <summary>One key/value setting (LLM key, JWT secret, GitHub PAT, …). Value is ciphertext.
/// Primary key is the <c>(TenantId, Key)</c> composite so each tenant owns its own values
/// (per-tenant LLM keys, per-tenant GitHub PAT, …); pre-tenant rows live under
/// <see cref="AgenticSdlc.Application.Identity.ITenantContext.DefaultTenantId"/>.</summary>
public sealed class AppConfigEntity
{
    /// <summary>Owning tenant — composite primary key alongside <see cref="Key"/>.</summary>
    public string TenantId { get; set; } = AgenticSdlc.Application.Identity.ITenantContext.DefaultTenantId;

    /// <summary>Configuration key, e.g. <c>Llm:Claude:ApiKey</c>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>DataProtection-encrypted value (base64 ciphertext).</summary>
    public string EncryptedValue { get; set; } = string.Empty;

    /// <summary>Last write timestamp (UTC).</summary>
    public DateTime UpdatedAtUtc { get; set; }
}
