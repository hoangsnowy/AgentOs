// Settings for the Admin REST client. Bound from the "Auth:Keycloak:Admin" config section. The
// Aspire AppHost forwards the dev master admin credentials; in production the operator points
// these at a service-account client with the realm-management role.

namespace AgenticSdlc.Modules.Tenants.Keycloak;

/// <summary>Keycloak admin REST settings.</summary>
public sealed class KeycloakAdminOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Auth:Keycloak:Admin";

    /// <summary>Base URL of the Keycloak server, e.g. <c>http://localhost:8080</c>.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Realm that holds the tenants, e.g. <c>agentic</c>.</summary>
    public string Realm { get; set; } = "agentic";

    /// <summary>Master-realm admin username (dev) or service-account client id (prod).</summary>
    public string Username { get; set; } = "admin";

    /// <summary>Master-realm admin password (dev) or service-account client secret (prod).</summary>
    public string Password { get; set; } = "admin";

    /// <summary>Client id used to obtain the admin access token.</summary>
    public string ClientId { get; set; } = "admin-cli";
}
