// Operator-only runtime configuration endpoints. The Web Settings UI calls these to rotate LLM
// keys and GitHub settings (the only keys in SettingsKeyRegistry) without restarting the API.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AgentOs.Modules.AppConfig.Endpoints;

internal static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        System.ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/settings/{prefix}", async (string prefix, IAppConfigStore store, CancellationToken ct) =>
        {
            if (!SettingsKeyRegistry.IsReadablePrefix(prefix))
            {
                return Results.Problem(
                    detail: $"'{prefix}' is not a readable settings prefix.",
                    statusCode: StatusCodes.Status400BadRequest);
            }
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
        .RequireAuthorization("Admin");

        app.MapPost("/settings", async (SetEntryRequest? body, IAppConfigStore store, CancellationToken ct) =>
        {
            if (body is null || string.IsNullOrWhiteSpace(body.Key))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["key"] = ["A non-empty setting key is required."],
                });
            }
            if (!SettingsKeyRegistry.IsKnownKey(body.Key))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["key"] = [$"'{body.Key}' is not a recognised runtime setting."],
                });
            }
            if (SettingsKeyRegistry.ValidateValue(body.Key, body.Value) is { } error)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["value"] = [error],
                });
            }
            await store.SetAsync(body.Key, body.Value, ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithName("SettingsSet")
        .WithSummary("Set a single key (overwrite if present)")
        .WithTags("Settings")
        .RequireAuthorization("Admin");

        app.MapDelete("/settings/{key}", async (string key, IAppConfigStore store, CancellationToken ct) =>
        {
            if (!SettingsKeyRegistry.IsKnownKey(key))
            {
                return Results.Problem(
                    detail: $"'{key}' is not a recognised runtime setting.",
                    statusCode: StatusCodes.Status400BadRequest);
            }
            await store.DeleteAsync(key, ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithName("SettingsDelete")
        .WithSummary("Delete a key")
        .WithTags("Settings")
        .RequireAuthorization("Admin");

        return app;
    }
}

/// <summary>Body for <c>POST /settings</c>.</summary>
public sealed record SetEntryRequest(string Key, string Value);
