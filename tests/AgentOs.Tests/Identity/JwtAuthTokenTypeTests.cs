// Guards the API bearer pipeline against Keycloak refresh/logout tokens being replayed as access
// tokens. Keycloak signs every token kind with the same realm key + audience, so issuer/audience/
// lifetime/signature pass for all of them — only the PAYLOAD `typ` claim ("Bearer" vs "Refresh"/
// "Logout") distinguishes an access token. These tests pin the discriminator (IsNonAccessToken) and
// the wiring (the registered OnTokenValidated event must Fail() a non-access token before its roles
// are trusted) using crafted, real-signed access vs refresh JWTs.

using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using AgentOs.Modules.Identity.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Identity;

public sealed class JwtAuthTokenTypeTests
{
    private const string Authority = "https://kc.example/realms/agentic";
    private const string Audience = "agentic-api";

    // ---- discriminator (IsNonAccessToken) ----

    [Fact]
    public void IsNonAccessToken_AccessTokenTypBearer_ReturnsFalse()
    {
        var principal = PrincipalWith(new Claim("typ", "Bearer"));
        JwtAuthExtensions.IsNonAccessToken(principal, out var typ).ShouldBeFalse();
        typ.ShouldBe("Bearer");
    }

    [Theory]
    [InlineData("Refresh")]
    [InlineData("Logout")]
    [InlineData("ID")]
    public void IsNonAccessToken_NonAccessTyp_ReturnsTrue(string tokenType)
    {
        var principal = PrincipalWith(new Claim("typ", tokenType));
        JwtAuthExtensions.IsNonAccessToken(principal, out var typ).ShouldBeTrue();
        typ.ShouldBe(tokenType);
    }

    [Fact]
    public void IsNonAccessToken_NoTypClaim_ReturnsFalse()
    {
        // A token without a `typ` payload claim (non-Keycloak callers) must NOT be locked out.
        var principal = PrincipalWith(new Claim("sub", "user-1"));
        JwtAuthExtensions.IsNonAccessToken(principal, out var typ).ShouldBeFalse();
        typ.ShouldBeNull();
    }

    [Theory]
    [InlineData("bearer")]
    [InlineData("BEARER")]
    public void IsNonAccessToken_BearerAnyCase_ReturnsFalse(string tokenType)
    {
        // Case-insensitive so a legitimately-cased access token is never rejected (lockout guard).
        var principal = PrincipalWith(new Claim("typ", tokenType));
        JwtAuthExtensions.IsNonAccessToken(principal, out _).ShouldBeFalse();
    }

    // ---- wiring: the registered OnTokenValidated event (crafted real-signed tokens) ----

    [Fact]
    public async Task OnTokenValidated_CraftedRefreshToken_FailsAuthentication()
    {
        var options = ConfiguredJwtBearerOptions();
        var refreshPrincipal = ClaimsFromCraftedJwt(tokenType: "Refresh");

        var ctx = await RunOnTokenValidatedAsync(options, refreshPrincipal);

        ctx.Result.ShouldNotBeNull();
        ctx.Result!.Succeeded.ShouldBeFalse();
        ctx.Result.Failure.ShouldNotBeNull();
        ctx.Result.Failure!.Message.ShouldContain("Refresh");
    }

    [Fact]
    public async Task OnTokenValidated_CraftedAccessToken_SucceedsAndFlattensRoles()
    {
        var options = ConfiguredJwtBearerOptions();
        var accessPrincipal = ClaimsFromCraftedJwt(
            tokenType: "Bearer",
            realmAccessJson: """{"roles":["admin","member"]}""");

        var ctx = await RunOnTokenValidatedAsync(options, accessPrincipal);

        // Access token: the event must NOT fail it, and must flatten realm_access into role claims.
        ctx.Result.ShouldBeNull();
        accessPrincipal.IsInRole("admin").ShouldBeTrue();
        accessPrincipal.IsInRole("member").ShouldBeTrue();
    }

    // ---- helpers ----

    private static ClaimsPrincipal PrincipalWith(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "test", nameType: "preferred_username", roleType: ClaimTypes.Role));

    // Resolve the JwtBearerOptions exactly as AddJwtAuth registers them, so the test exercises the
    // real OnTokenValidated delegate (not a hand-rolled copy).
    private static JwtBearerOptions ConfiguredJwtBearerOptions()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Development",
                ["Auth:Keycloak:Authority"] = Authority,
                ["Auth:Keycloak:Audience"] = Audience,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddJwtAuth(config);

        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
    }

    private static async Task<TokenValidatedContext> RunOnTokenValidatedAsync(
        JwtBearerOptions options, ClaimsPrincipal principal)
    {
        var scheme = new AuthenticationScheme(
            JwtBearerDefaults.AuthenticationScheme, displayName: null, handlerType: typeof(JwtBearerHandler));
        var ctx = new TokenValidatedContext(new DefaultHttpContext(), scheme, options)
        {
            Principal = principal,
        };
        await options.Events!.OnTokenValidated(ctx);
        return ctx;
    }

    // Mint a real HS256-signed JWT carrying issuer/audience plus a PAYLOAD `typ` claim — mirroring how
    // Keycloak stamps every token kind — then project its claims into a principal the way the bearer
    // handler would, so the discriminator reads the same claim graph it sees at runtime.
    private static ClaimsPrincipal ClaimsFromCraftedJwt(string tokenType, string? realmAccessJson = null)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes("agentos-unit-test-signing-key-0123456789-abcdef"));
        var claims = new Dictionary<string, object>
        {
            ["typ"] = tokenType,
            ["preferred_username"] = "alice",
        };
        if (realmAccessJson is not null)
        {
            claims["realm_access"] = realmAccessJson;
        }

        var jwt = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Authority,
            Audience = Audience,
            Claims = claims,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        });

        var parsed = new JsonWebToken(jwt);
        return new ClaimsPrincipal(
            new ClaimsIdentity(parsed.Claims, authenticationType: "test", nameType: "preferred_username", roleType: ClaimTypes.Role));
    }
}
