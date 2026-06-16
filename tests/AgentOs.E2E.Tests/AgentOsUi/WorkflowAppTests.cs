// AgentOS UI — the Workflow studio actually RUNS the drawn graph end-to-end in the browser. Same gate +
// fixture as the other desktop UI tests (RUN_AGENTOS_E2E=true, Web at AGENTOS_URL). The standalone Web has
// no LLM key, so the run rides the Offline failover provider (Llm:OfflineFallback) — proving the run-path
// works keyless. This is the coverage the audit found missing: every prior Workflow E2E only opened the
// window; these drive Run → live node status → completion, including the new control-flow + Human nodes.
//
// Window interactions are scoped to ".appwin.focused" (the WindowManagerService singleton stacks windows
// across runs), matching SpineAppTests.

using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace AgentOs.E2E.Tests.AgentOsUi;

public sealed class WorkflowAppTests : IClassFixture<AgentOsPageFixture>
{
    private readonly AgentOsPageFixture _fx;

    public WorkflowAppTests(AgentOsPageFixture fx) => _fx = fx;

    private async Task<ILocator> OpenWorkflowAsync()
    {
        await _fx.GotoDesktopAsync();
        await _fx.Page.Locator(".dicon", new() { HasTextString = "Workflow" }).First.ClickAsync();
        var win = _fx.Page.Locator(".appwin.focused");
        await Assertions.Expect(win.Locator(".appwin-title")).ToHaveTextAsync("Workflow");
        return win;
    }

    // Select a graph by its display name in the studio's graph dropdown, then wait for the canvas to repaint.
    private static async Task SelectGraphAsync(ILocator win, string name)
    {
        await win.Locator(".syn-select").SelectOptionAsync(new SelectOptionValue { Label = name });
        await Assertions.Expect(win.Locator(".syn-name")).ToHaveValueAsync(name);
    }

    // The seeded "5-Agent SDLC Pipeline" runs the real typed agents (Requirement→Coding→Testing→QA→End)
    // against the keyless Offline provider — nodes light up and the run completes.
    [Fact]
    public async Task Workflow_RunSdlcPipeline_Offline_CompletesWithNodesDone()
    {
        if (!AgentOsPageFixture.IsEnabled) { Assert.Skip(AgentOsPageFixture.SkipReason); }

        var win = await OpenWorkflowAsync();
        await SelectGraphAsync(win, "5-Agent SDLC Pipeline");

        await win.GetByRole(AriaRole.Button, new() { Name = "Run" }).ClickAsync();

        // The run log surfaces on Run; wait (web-first) for the terminal "complete" line.
        var log = win.Locator(".syn-log");
        await Assertions.Expect(log).ToContainTextAsync("Workflow complete",
            new LocatorAssertionsToContainTextOptions { Timeout = 60_000 });

        // The drawn nodes reached the Done run-state on the canvas (not a static mock).
        await Assertions.Expect(win.Locator(".step-node.run-done").First).ToBeVisibleAsync();
    }

    // The seeded "Control-Flow Demo" exercises Parallel fan-out, Merge fan-in, an If/Else branch, and a
    // Human checkpoint. The run pauses on the checkpoint card; approving it lets the run finish — proving
    // the new control-flow + human-in-the-loop nodes work end-to-end in the real UI.
    [Fact]
    public async Task Workflow_RunControlFlowDemo_Offline_PausesForHumanThenCompletes()
    {
        if (!AgentOsPageFixture.IsEnabled) { Assert.Skip(AgentOsPageFixture.SkipReason); }

        var win = await OpenWorkflowAsync();
        await SelectGraphAsync(win, "Control-Flow Demo");

        await win.GetByRole(AriaRole.Button, new() { Name = "Run" }).ClickAsync();

        // The Human node pauses the run — the checkpoint card appears. Approve it.
        var human = win.Locator(".syn-human");
        await Assertions.Expect(human).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 60_000 });
        await human.GetByRole(AriaRole.Button, new() { Name = "Approve" }).ClickAsync();

        // After approval the run completes.
        await Assertions.Expect(win.Locator(".syn-log")).ToContainTextAsync("Workflow complete",
            new LocatorAssertionsToContainTextOptions { Timeout = 60_000 });
    }
}
