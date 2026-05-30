// AgentOS UI — the Spine app (M2/M3): connected Workspaces, paired Runners, Sessions.
// Same gate + fixture as the other desktop UI tests: skipped unless RUN_AGENTOS_E2E=true with a
// Web running at AGENTOS_URL. The register-runner flow needs no DB — the one-time pairing token is
// minted by the real IRunnerPairingService, so the token panel appears even against Null repos.

using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace AgentOs.E2E.Tests.AgentOsUi;

public sealed class SpineAppTests : IClassFixture<AgentOsPageFixture>
{
    private readonly AgentOsPageFixture _fx;

    public SpineAppTests(AgentOsPageFixture fx) => _fx = fx;

    // Open the Spine app from its desktop icon and confirm the three panes render.
    [Fact]
    public async Task Spine_Opens_ShowsThreeTabs()
    {
        if (!AgentOsPageFixture.IsEnabled) { Assert.Skip(AgentOsPageFixture.SkipReason); }

        await _fx.GotoDesktopAsync();
        await _fx.Page.Locator(".dicon", new() { HasTextString = "Spine" }).First.ClickAsync();

        await Assertions.Expect(_fx.Page.Locator(".appwin .appwin-title:has-text(\"Spine\")")).ToBeVisibleAsync();
        await Assertions.Expect(_fx.Page.Locator(".spine-tab", new() { HasTextString = "Workspaces" })).ToBeVisibleAsync();
        await Assertions.Expect(_fx.Page.Locator(".spine-tab", new() { HasTextString = "Runners" })).ToBeVisibleAsync();
        await Assertions.Expect(_fx.Page.Locator(".spine-tab", new() { HasTextString = "Sessions" })).ToBeVisibleAsync();
    }

    // Register a runner and confirm the one-time pairing token (REMOTE_AGENT_ID/TOKEN) is shown.
    [Fact]
    public async Task Spine_RegisterRunner_ShowsOneTimePairingToken()
    {
        if (!AgentOsPageFixture.IsEnabled) { Assert.Skip(AgentOsPageFixture.SkipReason); }

        await _fx.GotoDesktopAsync();
        await _fx.Page.Locator(".dicon", new() { HasTextString = "Spine" }).First.ClickAsync();
        await Assertions.Expect(_fx.Page.Locator(".appwin .appwin-title:has-text(\"Spine\")")).ToBeVisibleAsync();

        // Runners is the default pane. Fill a label and register.
        await _fx.Page.GetByPlaceholder("Hoang's laptop").FillAsync("CI test runner");
        await _fx.Page.GetByRole(AriaRole.Button, new() { Name = "Register runner" }).ClickAsync();

        var token = _fx.Page.Locator(".admin-invite-url");
        await Assertions.Expect(token).ToBeVisibleAsync();
        await Assertions.Expect(token).ToContainTextAsync("REMOTE_AGENT_TOKEN");
        await Assertions.Expect(token).ToContainTextAsync("REMOTE_AGENT_ID");
    }
}
