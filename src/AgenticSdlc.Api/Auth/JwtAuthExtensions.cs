// AgenticSdlc.Api/Auth/JwtAuthExtensions.cs
// Phase 8 — Symmetric-key JWT bearer auth for the API. Single "operator" role for now;
// multi-tenant + RBAC ships in Horizon 1 (see docs/architecture/MIGRATION_BACKLOG.md M5).

using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace AgenticSdlc.Api.Auth;

/// <summary>DI + endpoint extensions for Phase 8 JWT bearer auth.</summary>
public static class JwtAuthExtensions
{
    /// <summary>Configuration section that holds <c>Issuer</c>, <c>Audience</c>, <c>Secret</c>, <c>OperatorPassword</c>.</summary>
    public const string SectionName = "Auth:Bearer";

    /// <summary>
    /// Adds JWT bearer authentication + an Operator policy. Wires <see cref="JwtBearerOptions"/>
    /// from the <c>Auth:Bearer</c> configuration section.
    /// </summary>
    public static IServiceCollection AddJwtAuth(this IServiceCollection services, IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        var section = config.GetSection(SectionName);
        var issuer = section["Issuer"] ?? "agentic-sdlc";
        var audience = section["Audience"] ?? "agentic-sdlc";
        var secret = section["Secret"] ?? "dev-only-secret-please-rotate-via-app-config-AT-LEAST-32-bytes";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(2),
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("Operator", policy => policy.RequireRole("operator"));
        });

        return services;
    }

    /// <summary>
    /// Maps <c>POST /auth/token</c>. Body: <c>{ user, password }</c>. The user is fixed at
    /// "operator" for Phase 8.1; the password is read from <c>Auth:Bearer:OperatorPassword</c>
    /// (which itself ships as a Phase-8.4 deliverable into the <c>app_config</c> DB).
    /// </summary>
    public static WebApplication MapAuthEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/auth/token", (LoginRequest body, IConfiguration config) =>
        {
            var section = config.GetSection(SectionName);
            var expected = section["OperatorPassword"] ?? "operator";
            if (!string.Equals(body.User, "operator", StringComparison.Ordinal) ||
                !string.Equals(body.Password, expected, StringComparison.Ordinal))
            {
                return Results.Unauthorized();
            }

            var secret = section["Secret"] ?? "dev-only-secret-please-rotate-via-app-config-AT-LEAST-32-bytes";
            var issuer = section["Issuer"] ?? "agentic-sdlc";
            var audience = section["Audience"] ?? "agentic-sdlc";

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var now = DateTime.UtcNow;
            var expires = now.AddHours(8);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: new[]
                {
                    new Claim(JwtRegisteredClaimNames.Sub, "operator"),
                    new Claim(ClaimTypes.Role, "operator"),
                    new Claim(JwtRegisteredClaimNames.Iat, new DateTimeOffset(now).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), ClaimValueTypes.Integer64),
                },
                notBefore: now,
                expires: expires,
                signingCredentials: creds);

            var jwt = new JwtSecurityTokenHandler().WriteToken(token);
            return Results.Ok(new TokenResponse(jwt, expires));
        })
        .WithName("AuthToken")
        .WithSummary("Phase 8 — exchange operator credentials for a JWT bearer token")
        .WithTags("Auth")
        .AllowAnonymous();

        return app;
    }
}

/// <summary>Body for <c>POST /auth/token</c>.</summary>
public sealed record LoginRequest(string User, string Password);

/// <summary>Response for <c>POST /auth/token</c>.</summary>
public sealed record TokenResponse(string Token, DateTime ExpiresAtUtc);
