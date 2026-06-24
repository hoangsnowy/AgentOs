// Unit coverage for the single source of identity reads off a ClaimsPrincipal: the tenant claim,
// the sub-then-NameIdentifier user id, and the admin role check. Every host + Blazor component now
// routes through these helpers, so a regression here is a repo-wide behaviour change.

using System.Security.Claims;
using AgentOs.SharedKernel.Identity;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Identity;

public sealed class ClaimsPrincipalExtensionsTests
{
    [Fact]
    public void GetTenantId_GetUserId_IsAdmin_ReadAllClaims()
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimNames.Tenant, "acme"),
                new Claim(ClaimNames.Subject, "user-1"),
                new Claim(ClaimTypes.Role, ClaimNames.AdminRole),
            },
            authenticationType: "test");
        var user = new ClaimsPrincipal(identity);

        user.GetTenantId().ShouldBe("acme");
        user.GetUserId().ShouldBe("user-1");
        user.IsAdmin().ShouldBeTrue();
    }

    [Fact]
    public void GetTenantId_NoTenantClaim_ReturnsNull()
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimNames.Subject, "user-1") },
            authenticationType: "test");

        new ClaimsPrincipal(identity).GetTenantId().ShouldBeNull();
    }

    [Fact]
    public void GetTenantId_EmptyTenantClaim_ReturnsNull()
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimNames.Tenant, string.Empty) },
            authenticationType: "test");

        new ClaimsPrincipal(identity).GetTenantId().ShouldBeNull();
    }

    [Fact]
    public void GetUserId_SubAbsent_FallsBackToNameIdentifier()
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "name-id-1") },
            authenticationType: "test");

        new ClaimsPrincipal(identity).GetUserId().ShouldBe("name-id-1");
    }

    [Fact]
    public void GetUserId_NoSubject_ReturnsNull()
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimNames.Tenant, "acme") },
            authenticationType: "test");

        new ClaimsPrincipal(identity).GetUserId().ShouldBeNull();
    }

    [Fact]
    public void IsAdmin_WithoutAdminRole_ReturnsFalse()
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimNames.Tenant, "acme"),
                new Claim(ClaimTypes.Role, ClaimNames.MemberRole),
            },
            authenticationType: "test");

        new ClaimsPrincipal(identity).IsAdmin().ShouldBeFalse();
    }
}
