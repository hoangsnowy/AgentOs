// Development-only auto-login. When Auth:DevAutoLogin is set (Development only — see Program.cs, which
// hard-throws if it is ever true outside Development), this scheme authenticates every request as a
// fixed "developer" principal so the AgentOS desktop runs with a single `dotnet run --project
// src/AgentOs.Web`, no Keycloak / Postgres required. The full Aspire stack turns it OFF (the AppHost
// injects Auth__DevAutoLogin=false) so it always uses real Keycloak OIDC. NEVER enabled in production.

using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentOs.Web.Auth;

/// <summary>Auto-authenticates every request as a fixed dev principal. Development only.</summary>
public sealed class DevAutoAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "DevAuto";

    public DevAutoAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Mirrors the claims a real Keycloak token carries (tenant + sub + username + flattened roles)
        // so ITenantContext, the desktop AuthorizeView, and the tenant-scoped UIs all behave normally.
        var claims = new[]
        {
            new Claim("sub", "dev-user"),
            new Claim(ClaimTypes.NameIdentifier, "dev-user"),
            new Claim("preferred_username", "developer"),
            new Claim(ClaimTypes.Name, "developer"),
            new Claim("tenant", "default"),
            new Claim(ClaimTypes.Role, "admin"),
            new Claim(ClaimTypes.Role, "member"),
        };
        var identity = new ClaimsIdentity(claims, SchemeName, ClaimTypes.Name, ClaimTypes.Role);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
