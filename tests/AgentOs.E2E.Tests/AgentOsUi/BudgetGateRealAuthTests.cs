// AgentOS UI — REAL-AUTH end-to-end for the PR #98/#100 budget gate against the full Aspire stack.
// Proves on the running app what unit tests can only simulate: with month-to-date spend over an ENFORCED
// cap, the Cost app reports the tenant "Over budget" — i.e. BudgetGuard, reading run_metrics
// tenant-explicitly from real Postgres, evaluates State=Exceeded. That is exactly the condition the pre-run
// gates act on, so this is the real-stack proof behind both gates:
//   - GraphExecutor's Workflow-studio gate (RunAsync returns a blocked GraphRunResult), and
//   - the /requirement·/code·/test·/qa endpoint gate (402).
// The deterministic "a run is actually blocked" behaviour is covered by GraphExecutorTests
// (RunAsync_BudgetExceeded_* and RunAsync_WorkflowSpend_IsMeteredAndTripsBudgetGateOnNextRun); driving the
// heavy Workflow-studio canvas under real-auth is flaky in the desktop harness, so it is intentionally not
// re-asserted here.
//
// Why it seeds spend: the offline failover provider (the keyless E2E path) always reports $0 cost, so a run
// can never push spend over a positive cap on its own. The test inserts a run_metrics row directly — the
// same shape GraphExecutor/PersistingOrchestratorAgent persist — a faithful stand-in for a keyed run's spend.
//
//   - Gates: RUN_AGENTOS_E2E_REAL=true (full stack up) AND AGENTOS_PG_CONN set to the AppHost Postgres
//     (Npgsql conn string for the `agentos` db) — skipped otherwise.
//   - Credentials: realm-seeded operator/operator (tenant=default).

using System;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Npgsql;
using Xunit;

namespace AgentOs.E2E.Tests.AgentOsUi;

public sealed class BudgetGateRealAuthTests : IClassFixture<AgentOsRealAuthFixture>
{
    private readonly AgentOsRealAuthFixture _fx;

    public BudgetGateRealAuthTests(AgentOsRealAuthFixture fx) => _fx = fx;

    private const string Tenant = "default";   // the seeded operator user's tenant claim

    // Insert one parent run + one run_metrics row carrying the given spend for this month, billed to the
    // explicit tenant — the same shape GraphExecutor now persists. run_metrics.RunId FKs pipeline_runs.Id,
    // so the parent row is required; ResultJson is NOT NULL.
    private static async Task SeedSpendAsync(string connectionString, string tenantId, decimal costUsd)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            WITH r AS (
              INSERT INTO pipeline.pipeline_runs
                ("Id","TenantId","UserStoryText","Status","TotalCostUsd","TotalTokensIn","TotalTokensOut","IterationCount","CreatedAtUtc","CompletedAtUtc","ResultJson")
              VALUES (gen_random_uuid(), @t, 'e2e budget seed', 'Done', @c, 100, 50, 1, now(), now(), '{}'::jsonb)
              RETURNING "Id")
            INSERT INTO pipeline.run_metrics
              ("TenantId","RunId","KcId","Iteration","AgentName","Model","Provider","TokensIn","TokensOut","LatencyMs","CostUsd","Success","TimestampUtc")
            SELECT @t, r."Id", 'KC1', 0, 'SeedAgent', 'seed-model', 'Claude', 100, 50, 50, @c, true, now() FROM r;
            """;
        cmd.Parameters.AddWithValue("t", tenantId);
        cmd.Parameters.AddWithValue("c", costUsd);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Budget_SpendOverEnforcedCap_CostApp_ReportsOverBudget()
    {
        if (!AgentOsRealAuthFixture.IsEnabled) { Assert.Skip(AgentOsRealAuthFixture.SkipReason); }
        var conn = Environment.GetEnvironmentVariable("AGENTOS_PG_CONN");
        if (string.IsNullOrWhiteSpace(conn))
        {
            Assert.Skip("Set AGENTOS_PG_CONN to the AppHost Postgres (agentos db) so the test can seed spend.");
        }

        // $10 of spend on record for the operator's tenant; the cap set below ($5) is under it.
        await SeedSpendAsync(conn!, Tenant, 10.00m);

        await _fx.LoginAsync();

        // Open the Cost app from its Dash (dock) button — the GNOME desktop is empty.
        await _fx.Page.Locator(".dock-item[title=\"Cost\"]").First.ClickAsync();
        var cost = _fx.Page.Locator(".appwin:has(.appwin-title:has-text(\"Cost\"))");
        await Assertions.Expect(cost).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // Set an enforced $5 cap.
        var capInput = cost.Locator(".budget-row input.prefs-input");
        await capInput.FillAsync("5");
        await capInput.PressAsync("Tab");   // blur → commit the @bind (onchange) so _cap = 5 before Save
        // The toggle's native checkbox is CSS-hidden behind the styled switch, so click the visible label
        // (which toggles it). Only when not already on — a prior run may have persisted enforce=on.
        var enforce = cost.Locator("label.toggle", new() { HasTextString = "Enforce" });
        if (!await enforce.Locator("input[type=checkbox]").IsCheckedAsync())
        {
            await enforce.ClickAsync();
        }
        await cost.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        // BudgetGuard, reading run_metrics tenant-explicitly on real Postgres, now reports the tenant over
        // its enforced cap ($10 of $5) — the exact State=Exceeded&&EnforceOn condition both pre-run gates use.
        await Assertions.Expect(cost).ToContainTextAsync("Over budget",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
        await Assertions.Expect(cost).ToContainTextAsync("of $5.0000 spent");
    }
}
