// MailHog HTTP API helper for the real-auth E2E suite. The full Aspire stack routes every dev email
// (Keycloak + the app's MailKit sender) into MailHog; its REST API lets a test ASSERT delivery
// instead of a human eyeballing an inbox. Default UI/API at http://localhost:8025 (AppHost mapping),
// overridable via MAILHOG_URL.

using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace AgentOs.E2E.Tests.AgentOsUi;

internal static class MailHog
{
    private static readonly HttpClient Http = new();

    public static string BaseUrl =>
        Environment.GetEnvironmentVariable("MAILHOG_URL") ?? "http://localhost:8025";

    /// <summary>Delete all captured messages so an assertion isn't polluted by earlier runs.</summary>
    public static async Task ClearAsync()
    {
        try { await Http.DeleteAsync($"{BaseUrl}/api/v1/messages").ConfigureAwait(false); }
        catch (HttpRequestException) { /* MailHog not up — the test that needs it will fail its poll */ }
    }

    /// <summary>Poll the MailHog inbox until a message whose JSON mentions every <paramref name="needles"/>
    /// appears, or the timeout elapses. Returns true on hit. Searching the raw JSON keeps this robust to
    /// MailHog's nested message schema (recipient address is never MIME-encoded, so it is the safe key).</summary>
    public static async Task<bool> WaitForMessageAsync(TimeSpan timeout, params string[] needles)
    {
        var deadlineTicks = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadlineTicks)
        {
            string json;
            try { json = await Http.GetStringAsync($"{BaseUrl}/api/v2/messages").ConfigureAwait(false); }
            catch (HttpRequestException) { json = string.Empty; }

            var all = needles.All(n => json.Contains(n, StringComparison.OrdinalIgnoreCase));
            if (all && needles.Length > 0) { return true; }

            await Task.Delay(500).ConfigureAwait(false);
        }
        return false;
    }
}
