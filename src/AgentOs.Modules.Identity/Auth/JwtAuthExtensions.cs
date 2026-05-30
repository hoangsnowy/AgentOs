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
        var requireHttps = bool.TryParse(kc["RequireHttpsMetadata"], out var rh) && rh;

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
                foreach (var role in roles.EnumerateArray())
                {
                    var name = role.GetString();
                    if (!string.IsNullOrEmpty(name))
                    {
                        identity.AddClaim(new Claim(ClaimTypes.Role, name));
                    }
                }
            }
        }
        catch (JsonException)
        {
        }
    }
}
