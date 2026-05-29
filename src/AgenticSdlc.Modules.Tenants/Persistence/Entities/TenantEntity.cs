// Tenant registry row. The runtime never reads it for auth — the OIDC token's "tenant" claim is the
// authoritative tenant id (see HttpTenantContext). This table records display metadata and
// creation provenance so the admin UI can list tenants, search them, and audit provisioning.

using System;

namespace AgenticSdlc.Modules.Tenants.Persistence.Entities;

/// <summary>One tenant in the system. Primary key = the same string used in OIDC token claims.</summary>
internal sealed class TenantEntity
{
    /// <summary>Tenant id — the Keycloak user-attribute value that lands in the access token's
    /// <c>tenant</c> claim. Lowercase kebab-case by convention.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Human-readable display name for admin UI / cost reports.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Provisioning timestamp (UTC).</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }
}
