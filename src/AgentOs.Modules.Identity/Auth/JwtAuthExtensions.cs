// JWT bearer auth — Keycloak OIDC resource server. Drives AddJwtBearer + Admin/Member policies.

using System;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace AgentOs.Modules.Identity.Auth;

/// <summary>DI extensions for Keycloak JWT bearer auth.</summary>
public static class JwtAuthExtensions
{
    /// <summary>Add JWT bearer authentication (Keycloak) + Admin/Member policies.</summary>
    public static IServiceCollection AddJwtAuth(this IServiceCollection services, IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        var kc = config.GetSection("Auth:Keycloak");
        var authority = kc["Authority"] ?? "http://localhost:8080/realms/agentic";
        var audience = kc["Audience"] ?? "agentic-api";
        // Default secure (true) — except for an http://localhost authority IN DEVELOPMENT, where
        // requiring HTTPS metadata makes the JwtBearer options-factory THROW on the first request and
        // turns every route (including /health and /alive) into a 500 on a standalone dev run. The
        // exception is environment-gated so a reverse-proxied production deploy that points its
        // authority at loopback still fails loud rather than silently skipping metadata validation.
        // Explicit config wins.
        var environment = config["ASPNETCORE_ENVIRONMENT"] ?? config["DOTNET_ENVIRONMENT"];
        var isDevelopment = string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase);
        var requireHttps = bool.TryParse(kc["RequireHttpsMetadata"], out var rh)
            ? rh
            : !(isDevelopment && IsLoopbackHttp(authority));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.Audience = audience;
                options.RequireHttpsMetadata = requireHttps;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = authority,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    RoleClaimType = ClaimTypes.Role,
                    NameClaimType = "preferred_username",
                    ClockSkew = TimeSpan.FromMinutes(2),
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = ctx => { FlattenRealmRoles(ctx.Principal); return Task.CompletedTask; },
                };
            });

        return services;
    }

    /// <summary>True when <paramref name="authority"/> is plain http on a loopback host (the dev
    /// Keycloak) — the only case where the HTTPS-metadata requirement defaults off.</summary>
    internal static bool IsLoopbackHttp(string authority) =>
        Uri.TryCreate(authority, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttp
        && uri.IsLoopback;

    /// <summary>Flatten Keycloak's nested <c>realm_access.roles</c> JSON into individual role claims.</summary>
    public static void FlattenRealmRoles(System.Security.Claims.ClaimsPrincipal? principal)
    {
        if (principal?.Identity is not ClaimsIdentity identity)
        {
            return;
        }
        var realmAccess = principal.FindFirst("realm_access")?.Value;
        if (string.IsNullOrEmpty(realmAccess))
        {
            return;
        }
        try
        {
            using var doc = JsonDocument.Parse(realmAccess);
            if (doc.RootElement.TryGetProperty("roles", out var roles) && roles.ValueKind == JsonValueKind.Array)
            {
                foreach (var name in roles.EnumerateArray()
                    .Select(r => r.GetString())
                    .Where(name => !string.IsNullOrEmpty(name)))
                {
                    identity.AddClaim(new Claim(ClaimTypes.Role, name!));
                }
            }
        }
        catch (JsonException ex)
        {
            // Malformed realm_access claim — the user simply gets no role claims.
            _ = ex.Message;
        }
    }
}
