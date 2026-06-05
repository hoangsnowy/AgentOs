namespace AgentOs.Web.Services;

/// <summary>Whether dev auto-login is active for this host. Gates dev-only UI affordances such as the
/// TopBar "View as" role-preview menu, which is meaningless (and absent) under real Keycloak auth.</summary>
public sealed record DevModeState(bool DevAutoLogin);
