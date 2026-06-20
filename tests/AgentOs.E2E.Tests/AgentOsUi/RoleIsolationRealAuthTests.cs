// REAL-AUTH E2E — role isolation, fully automated. The seeded `member` user (role=member) logs in via
// real Keycloak and must NOT see the admin-only apps (Users, Evidence), while a normal app (Spine) is
// visible. This replaces the manual "log in as member and eyeball the desktop" check.
// Gated by RUN_AGENTOS_E2E_REAL=true with the full Aspire stack running and the realm imported fresh
// (the `member`/`member` user comes from infra/keycloak/agentic-realm.json).

using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace AgentOs.E2E.Tests.AgentOsUi;

public sealed class RoleIsolationRealAuthTests : IClassFixture<AgentOsRealAuthFixture>
{
    private readonly AgentOsRealAuthFixture _fx;

    public RoleIsolationRealAuthTests(AgentOsRealAuthFixture fx) => _fx = fx;

    // A member sees normal apps but not the admin-only ones.
    [Fact]
    public async Task Member_RealAuth_AdminAppsHidden()
    {
        if (!AgentOsRealAuthFixture.IsEnabled) { Assert.Skip(AgentOsRealAuthFixture.SkipReason); }

        await _fx.LoginAsync("member", "member");

        // Desktop renders (no circuit crash — guards the IssueWorkAgent eager-ctor regression too).
        await Assertions.Expect(_fx.Page.Locator(".dock")).ToBeVisibleAsync();

        // Spine is visible to every member.
        await Assertions.Expect(_fx.Page.Locator(".dock-item[title=\"Spine\"]").First).ToBeVisibleAsync();

        // AdminOnly apps must be absent from the member's app surface.
        await Assertions.Expect(_fx.Page.Locator(".dock-item[title=\"Users\"]")).ToHaveCountAsync(0);
        await Assertions.Expect(_fx.Page.Locator(".dock-item[title=\"Evidence\"]")).ToHaveCountAsync(0);
    }
}
