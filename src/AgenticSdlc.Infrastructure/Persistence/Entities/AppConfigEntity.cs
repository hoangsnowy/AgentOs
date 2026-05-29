// AgenticSdlc.Infrastructure/Persistence/Entities/AppConfigEntity.cs
// Phase 8.4b — runtime-mutable configuration row. Value is encrypted at rest via ASP.NET
// DataProtection before it is written; EfAppConfigStore decrypts on read.

using System;

namespace AgenticSdlc.Infrastructure.Persistence.Entities;

/// <summary>One key/value setting (LLM key, JWT secret, GitHub PAT, …). Value is ciphertext.</summary>
public sealed class AppConfigEntity
{
    /// <summary>Configuration key, e.g. <c>Llm:Claude:ApiKey</c>. Primary key.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>DataProtection-encrypted value (base64 ciphertext).</summary>
    public string EncryptedValue { get; set; } = string.Empty;

    /// <summary>Last write timestamp (UTC).</summary>
    public DateTime UpdatedAtUtc { get; set; }
}
