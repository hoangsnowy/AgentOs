// HttpTenantContext edge cases: missing tenant claim falls back to DefaultTenantId, principal
// with multiple tenant claims yields the first, role + admin reads work off ClaimTypes.Role.

using System.Collections.Generic;
using System.Security.Claims;
using AgentOs.Modules.Identity;
using AgentOs.SharedKernel.Identity;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Identity;

public sealed class HttpTenantContextTests
{
    [Fact]
    public void TenantId_NoClaim_FallsBackToDefault()
    {
        var ctx = NewContext(new ClaimsPrincipal(new ClaimsIdentity()));
        ctx.TenantId.ShouldBe(ITenantContext.DefaultTenantId);
        ctx.IsAuthenticated.ShouldBeFalse();
    }

    [Fact]
    public void TenantId_AuthenticatedButNoClaim_FailsClosedEmpty()
    {
        // A signed-in principal with no tenant claim must NOT inherit the shared `default` tenant.
        var identity = new ClaimsIdentity(new[] { new Claim("sub", "user-1") }, authenticationType: "test");
        var ctx = NewContext(new ClaimsPrincipal(identity));
        ctx.IsAuthenticated.ShouldBeTrue();
        ctx.TenantId.ShouldBe(string.Empty);
    }

    [Fact]
    public void TenantId_FromClaim_ReadsValue()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("tenant", "acme"),
            new Claim("sub", "user-1"),
            new Claim("preferred_username", "alice"),
        }, authenticationType: "test");
        var ctx = NewContext(new ClaimsPrincipal(identity));
        ctx.TenantId.ShouldBe("acme");
        ctx.UserId.ShouldBe("user-1");
        ctx.UserName.ShouldBe("alice");
        ctx.IsAuthenticated.ShouldBeTrue();
    }

    [Fact]
    public void Roles_FromRoleClaims_FlattenedList()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("tenant", "acme"),
            new Claim(ClaimTypes.Role, "admin"),
            new Claim(ClaimTypes.Role, "member"),
        }, authenticationType: "test");
        var ctx = NewContext(new ClaimsPrincipal(identity));
        ctx.Roles.ShouldContain("admin");
        ctx.Roles.ShouldContain("member");
        ctx.IsAdmin.ShouldBeTrue();
    }

    [Fact]
    public void IsAdmin_WithoutAdminRole_False()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("tenant", "acme"),
            new Claim(ClaimTypes.Role, "member"),
        }, authenticationType: "test");
        var ctx = NewContext(new ClaimsPrincipal(identity));
        ctx.IsAdmin.ShouldBeFalse();
    }

    [Fact]
    public void TenantId_MultipleClaims_TakesFirst()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("tenant", "first"),
            new Claim("tenant", "second"),
        }, authenticationType: "test");
        var ctx = NewContext(new ClaimsPrincipal(identity));
        ctx.TenantId.ShouldBe("first");
    }

    private static HttpTenantContext NewContext(ClaimsPrincipal user)
    {
        var httpCtx = new DefaultHttpContext { User = user };
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpCtx);
        return new HttpTenantContext(accessor);
    }
}
