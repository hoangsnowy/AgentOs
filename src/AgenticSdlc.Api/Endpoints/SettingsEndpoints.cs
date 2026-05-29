// AgenticSdlc.Api/Endpoints/SettingsEndpoints.cs
// Phase 8.4 — operator-only runtime configuration endpoints. The Web Settings UI calls these to
// rotate LLM keys, the JWT signing secret, the operator password, etc. without restarting the API.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Application.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace AgenticSdlc.Api.Endpoints;

/// <summary>Maps the Phase 8.4 /settings/* endpoints.</summary>
public static class SettingsEndpoints
{
    /// <summary>Mount the endpoints onto <paramref name="app"/>.</summary>
    public static WebApplication MapSettingsEndpoints(this WebApplication app)
    {
        System.ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/settings/{prefix}", async (string prefix, IAppConfigStore store, CancellationToken ct) =>
        {
            var keys = await store.ListAsync(prefix + ":", ct).ConfigureAwait(false);
            var dict = new Dictionary<string, string?>(keys.Count);
            foreach (var k in keys)
            {
                dict[k] = await store.GetAsync(k, ct).ConfigureAwait(false);
            }
            return Results.Ok(dict);
        })
        .WithName("SettingsList")
        .WithSummary("Read every key beneath a prefix (Llm, Auth, Github, …)")
        .WithTags("Settings")
        .RequireAuthorization();

        app.MapPost("/settings", async (SetEntryRequest body, IAppConfigStore store, CancellationToken ct) =>
        {
            await store.SetAsync(body.Key, body.Value, ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithName("SettingsSet")
        .WithSummary("Set a single key (overwrite if present)")
        .WithTags("Settings")
        .RequireAuthorization();

        app.MapDelete("/settings/{key}", async (string key, IAppConfigStore store, CancellationToken ct) =>
        {
            await store.DeleteAsync(key, ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithName("SettingsDelete")
        .WithSummary("Delete a key")
        .WithTags("Settings")
        .RequireAuthorization();

        return app;
    }
}

/// <summary>Body for <c>POST /settings</c>.</summary>
public sealed record SetEntryRequest(string Key, string Value);
