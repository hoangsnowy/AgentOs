// AgentOS UI — the Spine app (M2/M3): connected Workspaces, paired Runners, Sessions.
// Same gate + fixture as the other desktop UI tests: skipped unless RUN_AGENTOS_E2E=true with a
// Web running at AGENTOS_URL. The register-runner flow needs no DB — the one-time pairing token is
// minted by the real IRunnerPairingService, so the token panel appears even against Null repos.
//
// All window interactions are scoped to ".appwin.focused" (the newest, top-most window). The Web's
// WindowManagerService is a singleton, so windows opened by earlier tests/runs persist and stack;
// scoping to the focused window keeps these tests robust against that accumulation.

using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace AgentOs.E2E.Tests.AgentOsUi;

public sealed class SpineAppTests : IClassFixture<AgentOsPageFixture>
{
    private readonly AgentOsPageFixture _fx;

    public SpineAppTests(AgentOsPageFixture fx) => _fx = fx;

    private async Task<ILocator> OpenSpineAsync()
    {
        await _fx.GotoDesktopAsync();
        await _fx.Page.Locator(".dicon", new() { HasTextString = "Spine" }).First.ClickAsync();
        var win = _fx.Page.Locator(".appwin.focused");
        await Assertions.Expect(win.Locator(".appwin-title")).ToHaveTextAsync("Spine");
        return win;
    }

    // Open the Spine app from its desktop icon and confirm the three panes render.
    [Fact]
    public async Task Spine_Opens_ShowsThreeTabs()
    {
        if (!AgentOsPageFixture.IsEnabled) { Assert.Skip(AgentOsPageFixture.SkipReason); }

        var win = await OpenSpineAsync();

        await Assertions.Expect(win.Locator(".spine-tab", new() { HasTextString = "Workspaces" })).ToBeVisibleAsync();
        await Assertions.Expect(win.Locator(".spine-tab", new() { HasTextString = "Runners" })).ToBeVisibleAsync();
        await Assertions.Expect(win.Locator(".spine-tab", new() { HasTextString = "Sessions" })).ToBeVisibleAsync();
    }

    // The Workspaces tab shows the connect-a-repository form.
    [Fact]
    public async Task Spine_WorkspacesTab_ShowsConnectForm()
    {
        if (!AgentOsPageFixture.IsEnabled) { Assert.Skip(AgentOsPageFixture.SkipReason); }

        var win = await OpenSpineAsync();
        await win.Locator(".spine-tab", new() { HasTextString = "Workspaces" }).ClickAsync();

        await Assertions.Expect(win.GetByPlaceholder("ghp_… / Azure PAT")).ToBeVisibleAsync();
        await Assertions.Expect(win.GetByRole(AriaRole.Button, new() { Name = "Connect" })).ToBeVisibleAsync();
    }

    // Register a runner and confirm the one-time pairing token (REMOTE_AGENT_ID/TOKEN) is shown.
    [Fact]
    public async Task Spine_RegisterRunner_ShowsOneTimePairingToken()
    {
        if (!AgentOsPageFixture.IsEnabled) { Assert.Skip(AgentOsPageFixture.SkipReason); }

        var win = await OpenSpineAsync();

        // Runners is the default pane. Fill a label and register. (The register controls sit in the body
        // interior, clear of the window's thin edge resize-handles.)
        await win.GetByPlaceholder("Hoang's laptop").FillAsync("CI test runner");
        await win.GetByRole(AriaRole.Button, new() { Name = "Register runner" }).ClickAsync();

        var token = win.Locator(".admin-invite-url");
        await Assertions.Expect(token).ToBeVisibleAsync();
        await Assertions.Expect(token).ToContainTextAsync("REMOTE_AGENT_TOKEN");
        await Assertions.Expect(token).ToContainTextAsync("REMOTE_AGENT_ID");
    }
}
