// E2E: boots the full Aspire app (Postgres + API + Web) and hits the API over HTTP.
// Catches publish/orchestration-only failures that unit tests cannot see.
// Requires Docker (Postgres container). Skipped unless RUN_E2E=true.
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Xunit;

namespace AgenticSdlc.E2E.Tests;

public sealed class AspireApiTests
{
    [Fact]
    public async Task Api_Health_ReturnsSuccess_WhenOrchestratedByAspire()
    {
        if (Environment.GetEnvironmentVariable("RUN_E2E") != "true")
        {
            Assert.Skip("E2E: set RUN_E2E=true (needs Docker for the Postgres container) to run.");
        }

        var ct = TestContext.Current.CancellationToken;
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AgenticSdlc_AppHost>(ct);
        await using var app = await appHost.BuildAsync(ct);
        await app.StartAsync(ct);

        await app.ResourceNotifications
            .WaitForResourceHealthyAsync("api", ct)
            .WaitAsync(TimeSpan.FromMinutes(3), ct);

        using var http = app.CreateHttpClient("api");
        var response = await http.GetAsync("/health", ct);

        Assert.True(response.IsSuccessStatusCode, $"/health returned {(int)response.StatusCode}");
    }
}
