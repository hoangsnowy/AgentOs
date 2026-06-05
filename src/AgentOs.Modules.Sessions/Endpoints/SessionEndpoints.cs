// M3 — Sessions + Runners HTTP endpoints. Create a member × workspace session, list/get/close them;
// register a runner (a paired dev machine) and revoke it. Auth: Member policy; tenant + member resolved
// from the token by ITenantContext.
//
// Secret handling: a runner's pairing token is generated server-side, its salted hash is persisted on
// the runner row, and the plaintext is returned EXACTLY ONCE in the create response. It is never stored
// and never appears in any list/get DTO — the member pastes it into their runner's REMOTE_AGENT_TOKEN.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Sessions;
using AgentOs.Modules.Sessions.Pairing;
using AgentOs.Modules.Sessions.Persistence;
using AgentOs.Modules.Sessions.Persistence.Entities;
using AgentOs.SharedKernel.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AgentOs.Modules.Sessions.Endpoints;

internal static class SessionEndpoints
{
    public static void MapSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var sessions = endpoints.MapGroup("/sessions").RequireAuthorization("Member");
        sessions.MapGet(string.Empty, ListSessionsAsync);
        sessions.MapGet("/{id:guid}", GetSessionAsync);
        sessions.MapPost(string.Empty, CreateSessionAsync);
        sessions.MapPost("/{id:guid}/close", CloseSessionAsync);

        var runners = endpoints.MapGroup("/runners").RequireAuthorization("Member");
        runners.MapGet(string.Empty, ListRunnersAsync);
        runners.MapPost(string.Empty, RegisterRunnerAsync);
        runners.MapPost("/{id:guid}/revoke", RevokeRunnerAsync);
    }

    // ---- Sessions ----

    private static async Task<IResult> ListSessionsAsync(ISessionRepository repo, CancellationToken ct)
    {
        var rows = await repo.ListAsync(ct).ConfigureAwait(false);
        return Results.Ok(rows.Select(SessionDto.From).ToList());
    }

    private static async Task<IResult> GetSessionAsync(Guid id, ISessionRepository repo, CancellationToken ct)
    {
        var row = await repo.GetAsync(id, ct).ConfigureAwait(false);
        return row is null ? Results.NotFound() : Results.Ok(SessionDto.From(row));
    }

    private static async Task<IResult> CreateSessionAsync(
        CreateSessionRequest request,
        ISessionRepository repo,
        ITenantContext tenant,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Title) || request.WorkspaceId == Guid.Empty)
        {
            return Results.BadRequest("title and workspaceId are required.");
        }

        var entity = new RemoteSessionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.TenantId,
            WorkspaceId = request.WorkspaceId,
            MemberUserId = tenant.UserId ?? string.Empty,
            Title = request.Title,
            Status = "Draft",
            CreatedAtUtc = clock.GetUtcNow(),
            CreatedByUserId = tenant.UserId,
        };
        await repo.AddAsync(entity, ct).ConfigureAwait(false);

        return Results.Created($"/sessions/{entity.Id}", SessionDto.From(entity));
    }

    private static async Task<IResult> CloseSessionAsync(
        Guid id, ISessionRepository repo, TimeProvider clock, CancellationToken ct)
    {
        var closed = await repo.CloseAsync(id, clock.GetUtcNow(), ct).ConfigureAwait(false);
        return closed ? Results.NoContent() : Results.NotFound();
    }

    // ---- Runners ----

    private static async Task<IResult> ListRunnersAsync(IRunnerRepository repo, CancellationToken ct)
    {
        var rows = await repo.ListAsync(ct).ConfigureAwait(false);
        return Results.Ok(rows.Select(RunnerDto.From).ToList());
    }

    private static async Task<IResult> RegisterRunnerAsync(
        RegisterRunnerRequest request,
        IRunnerProvisioningService provisioning,
        ITenantContext tenant,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Label))
        {
            return Results.BadRequest("label is required.");
        }

        // Same provisioning path the VS Code browser-pairing flow uses. The plaintext token is
        // returned ONCE here and never again.
        var r = await provisioning
            .ProvisionAsync(tenant.TenantId, tenant.UserId ?? string.Empty, request.Label, ct)
            .ConfigureAwait(false);

        return Results.Created($"/runners/{r.RunnerId}", new RunnerCreatedDto(r.RunnerId, r.Label, r.Token, r.Status));
    }

    private static async Task<IResult> RevokeRunnerAsync(Guid id, IRunnerRepository repo, CancellationToken ct)
    {
        var ok = await repo.SetStatusAsync(id, "Revoked", ct).ConfigureAwait(false);
        return ok ? Results.NoContent() : Results.NotFound();
    }
}

/// <summary>Create-a-session request body.</summary>
internal sealed record CreateSessionRequest(Guid WorkspaceId, string Title);

/// <summary>Register-a-runner request body.</summary>
internal sealed record RegisterRunnerRequest(string Label);

/// <summary>Session projection returned to clients.</summary>
internal sealed record SessionDto(
    Guid Id,
    Guid WorkspaceId,
    string MemberUserId,
    string Title,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ClosedAtUtc)
{
    public static SessionDto From(RemoteSessionEntity e) => new(
        e.Id, e.WorkspaceId, e.MemberUserId, e.Title, e.Status, e.CreatedAtUtc, e.ClosedAtUtc);
}

/// <summary>Runner projection returned to clients — never carries the token or its hash.</summary>
internal sealed record RunnerDto(
    Guid Id,
    string OwnerUserId,
    string Label,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastSeenUtc)
{
    public static RunnerDto From(RunnerEntity e) => new(
        e.Id, e.OwnerUserId, e.Label, e.Status, e.CreatedAtUtc, e.LastSeenUtc);
}

/// <summary>Register-a-runner response — the only place the plaintext pairing token is ever returned.</summary>
internal sealed record RunnerCreatedDto(Guid RunnerId, string Label, string PairingToken, string Status);
