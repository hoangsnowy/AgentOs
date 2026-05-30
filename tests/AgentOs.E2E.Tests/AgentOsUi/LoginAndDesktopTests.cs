// AgentOS UI — login overlay + desktop icons + right-click context menu.
// Covers test-plan scenarios 1, 2, 3.

using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace AgentOs.E2E.Tests.AgentOsUi;

public sealed class LoginAndDesktopTests : IClassFixture<AgentOsPageFixture>
{
    private readonly AgentOsPageFixture _fx;

    public LoginAndDesktopTests(AgentOsPageFixture fx) => _fx = fx;

    // Scenario 1: Login overlay shows on first visit; Sign in persists + hides overlay.
    [Fact]
    public async Task LoginOverlay_ShowsOnFirstVisit_AndSignInPersists()
    {
        if (!AgentOsPageFixture.IsEnabled) { Assert.Skip(AgentOsPageFixture.SkipReason); }

        await _fx.ClearAuthAsync();
        await _fx.Page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });

        var overlay = _fx.Page.Locator(".login-overlay");
        await Assertions.Expect(overlay).ToBeVisibleAsync();

        // Username pre-filled with "developer"; click Sign in.
        await _fx.Page.Locator(".login-card .btn-primary").ClickAsync();

        await Assertions.Expect(overlay).ToBeHiddenAsync();

        var signed = await _fx.Page.EvaluateAsync<string?>("localStorage.getItem('agentic-signed-in')");
        Assert.Equal("developer", signed);
    }

    // Scenario 2: right-click on the desktop opens the context menu at cursor position.
    [Fact]
    public async Task RightClick_OpensContextMenu_AtCursor_AndCloseOnOutsideClick()
    {
        if (!AgentOsPageFixture.IsEnabled) { Assert.Skip(AgentOsPageFixture.SkipReason); }

        await _fx.GotoDesktopAsync();

        var desktop = _fx.Page.Locator(".desktop");
        await desktop.ClickAsync(new LocatorClickOptions { Button = MouseButton.Right, Position = new Position { X = 300, Y = 250 } });

        var ctx = _fx.Page.Locator(".ctxmenu");
        await Assertions.Expect(ctx).ToBeVisibleAsync();

        // Position is set from MouseEvent.ClientX/Y; should be near our click point.
        var box = await ctx.BoundingBoxAsync();
        Assert.NotNull(box);
        Assert.InRange(box!.X, 200, 400);
        Assert.InRange(box.Y, 150, 350);

        // Click the backdrop to dismiss.
        await _fx.Page.Locator(".ctxmenu-backdrop").ClickAsync();
        await Assertions.Expect(ctx).ToBeHiddenAsync();
    }

    // Scenario 3: clicking a Desktop icon opens an AppFrame for the corresponding app key.
    [Theory]
    [InlineData("Pipeline")]
    [InlineData("Workflow")]
    [InlineData("Spine")]
    [InlineData("Settings")]
    [InlineData("System")]
    public async Task DesktopIcon_Click_OpensAppWindow(string title)
    {
        if (!AgentOsPageFixture.IsEnabled) { Assert.Skip(AgentOsPageFixture.SkipReason); }

        await _fx.GotoDesktopAsync();

        // Find the dicon button whose .dicon-title text matches.
        var icon = _fx.Page.Locator(".dicon", new() { HasTextString = title }).First;
        await icon.ClickAsync();

        // AppFrame renders with a titlebar that contains the title text.
        var winTitle = _fx.Page.Locator($".appwin .appwin-title:has-text(\"{title}\")");
        await Assertions.Expect(winTitle).ToBeVisibleAsync();
    }
}
