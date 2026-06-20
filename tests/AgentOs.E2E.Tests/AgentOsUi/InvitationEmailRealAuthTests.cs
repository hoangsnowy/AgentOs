// REAL-AUTH E2E — the invitation-email path end-to-end, fully automated (no human checking an inbox).
// operator logs in via real Keycloak → Users app → mint an invite for a unique address → the app's
// MailKit sender delivers to MailHog → we assert the message landed via the MailHog API.
// Gated by RUN_AGENTOS_E2E_REAL=true with the full Aspire stack running (Web + Keycloak + MailHog).

using System;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace AgentOs.E2E.Tests.AgentOsUi;

public sealed class InvitationEmailRealAuthTests : IClassFixture<AgentOsRealAuthFixture>
{
    private readonly AgentOsRealAuthFixture _fx;

    public InvitationEmailRealAuthTests(AgentOsRealAuthFixture fx) => _fx = fx;

    // Mint an invite with an email address through the Users app and assert the message reaches MailHog.
    [Fact]
    public async Task Invite_RealAuth_EmailLandsInMailHog()
    {
        if (!AgentOsRealAuthFixture.IsEnabled) { Assert.Skip(AgentOsRealAuthFixture.SkipReason); }

        await MailHog.ClearAsync();
        await _fx.LoginAsync(); // operator / admin

        // Open the (admin-only) Users app from its Dash (dock) button.
        await _fx.Page.Locator(".dock-item[title=\"Users\"]").First.ClickAsync();
        var win = _fx.Page.Locator(".appwin.focused");
        await Assertions.Expect(win.Locator(".appwin-title")).ToHaveTextAsync("Users");

        // Mint an invite for a unique address so the MailHog assertion can't collide with other runs.
        var email = $"e2e-invite-{Guid.NewGuid():N}@agentic.local";
        await win.GetByPlaceholder("bob@acme.test").FillAsync(email);
        await win.GetByRole(AriaRole.Button, new() { Name = "Mint invite URL" }).ClickAsync();

        // UI confirms the invite URL was minted.
        await Assertions.Expect(win.Locator(".cred-readout textarea")).ToContainTextAsync("/signup?invite=");

        // The real assertion: the MailKit sender delivered the invite to MailHog for that address.
        var delivered = await MailHog.WaitForMessageAsync(TimeSpan.FromSeconds(20), email);
        Assert.True(delivered, $"No invitation email for {email} arrived in MailHog within 20s.");
    }
}
