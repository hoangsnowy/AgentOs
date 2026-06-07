// M2 / board reshape — Workspaces HTTP endpoints. Connect a GitHub Projects v2 board (or, later, an
// ADO board) as a workspace, list/get/remove boards, manage the repos under a board, list connectable
// repos/boards for a token, and read a repo's context to ground the Requirement agent. Auth: Member
// policy; tenant resolved from the token by ITenantContext.
//
// Secret handling: the access token is validated, then stored ONLY in the encrypted AppConfig store
// under the board's CredentialRef. It is never written to a row and never returned in any DTO.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Workspaces;
using AgentOs.Modules.AppConfig;
using AgentOs.Modules.Workspaces.Persistence;
using AgentOs.Modules.Workspaces.Persistence.Entities;
using AgentOs.SharedKernel.Identity;
using AgentOs.SharedKernel.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AgentOs.Modules.Workspaces.Endpoints;

internal static class WorkspaceEndpoints
{
    public static void MapWorkspaceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/workspaces").RequireAuthorization("Member");

        group.MapGet(string.Empty, ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPost(string.Empty, ConnectAsync);
        group.MapDelete("/{id:guid}", RemoveAsync);
        group.MapPost("/repos", ListReposForTokenAsync);
        group.MapPost("/boards", ListBoardsForTokenAsync);
        group.MapGet("/{id:guid}/repos", ListBoardReposAsync);
        group.MapPost("/{id:guid}/repos", AddRepoAsync);
        group.MapDelete("/{id:guid}/repos/{repoId:guid}", RemoveRepoAsync);
        group.MapGet("/{id:guid}/context", ContextAsync);
    }

    private static async Task<IResult> ListAsync(IWorkspaceRepository repo, CancellationToken ct, int? limit = null, int? offset = null)
    {
        var rows = await repo.ListAsync(limit ?? Page.DefaultLimit, offset ?? 0, ct).ConfigureAwait(false);
        return Results.Ok(rows.Select(WorkspaceDto.From).ToList());
    }

    private static async Task<IResult> GetAsync(Guid id, IWorkspaceRepository repo, CancellationToken ct)
    {
        var row = await repo.GetAsync(id, ct).ConfigureAwait(false);
        return row is null ? Results.NotFound() : Results.Ok(WorkspaceDto.From(row));
    }

    private static async Task<IResult> ConnectAsync(
        ConnectWorkspaceRequest request,
        IWorkspaceConnector connector,
        ITenantContext tenant,
        CancellationToken ct)
    {
        if (request is null)
        {
            return Results.BadRequest("A request body is required.");
        }

        var input = new WorkspaceConnectInput(
            request.Name, request.Kind, request.ProjectOwner, request.ProjectScope,
            request.ProjectNumber, request.Project, request.Host, request.AccessToken);
        var result = await connector.ConnectAsync(tenant.TenantId, tenant.UserId, input, ct).ConfigureAwait(false);

        return result.Ok && result.Workspace is not null
            ? Results.Created($"/workspaces/{result.Workspace.Id}", WorkspaceDto.From(result.Workspace))
            : Results.BadRequest(result.Error ?? "Could not connect the board.");
    }

    private static async Task<IResult> RemoveAsync(
        Guid id, IWorkspaceRepository repo, IAppConfigStore credentials, CancellationToken ct)
    {
        var row = await repo.GetAsync(id, ct).ConfigureAwait(false);
        if (row is null)
        {
            return Results.NotFound();
        }
        var removed = await repo.RemoveAsync(id, ct).ConfigureAwait(false);
        if (removed)
        {
            await credentials.DeleteAsync(row.CredentialRef, ct).ConfigureAwait(false);
        }
        return Results.NoContent();
    }

    private static async Task<IResult> ListReposForTokenAsync(
        ListReposRequest request,
        ISourceProviderResolver providers,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.AccessToken))
        {
            return Results.BadRequest("accessToken is required.");
        }
        if (!providers.TryResolve(request.Kind, out var provider) || provider is null)
        {
            return Results.BadRequest($"No source provider registered for '{request.Kind}'.");
        }

        var creds = new ConnectionCredentials(request.Kind, request.AccessToken, request.Owner, request.Host);
        var repos = await provider.ListRepositoriesAsync(creds, ct).ConfigureAwait(false);
        return Results.Ok(repos);
    }

    private static async Task<IResult> ListBoardsForTokenAsync(
        ListReposRequest request,
        ISourceProviderResolver providers,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.AccessToken))
        {
            return Results.BadRequest("accessToken is required.");
        }
        if (!providers.TryResolve(request.Kind, out var provider) || provider is null)
        {
            return Results.BadRequest($"No source provider registered for '{request.Kind}'.");
        }

        var creds = new ConnectionCredentials(request.Kind, request.AccessToken, request.Owner, request.Host);
        var boards = await provider.ListBoardsAsync(creds, ct).ConfigureAwait(false);
        return Results.Ok(boards);
    }

    private static async Task<IResult> ListBoardReposAsync(
        Guid id, IWorkspaceRepository repo, ITenantContext tenant, CancellationToken ct)
    {
        var repos = await repo.ListReposForTenantAsync(tenant.TenantId, id, ct).ConfigureAwait(false);
        return Results.Ok(repos.Select(RepoDto.From).ToList());
    }

    private static async Task<IResult> AddRepoAsync(
        Guid id, AddRepoRequest request, IWorkspaceConnector connector, ITenantContext tenant, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Owner) || string.IsNullOrWhiteSpace(request.Repo))
        {
            return Results.BadRequest("owner and repo are required.");
        }
        var result = await connector.AddRepoAsync(tenant.TenantId, id, request.Owner, request.Repo, request.DefaultBranch, ct)
            .ConfigureAwait(false);
        return result.Ok && result.Repo is not null
            ? Results.Created($"/workspaces/{id}/repos/{result.Repo.Id}", RepoDto.From(result.Repo))
            : Results.BadRequest(result.Error ?? "Could not add the repository.");
    }

    private static async Task<IResult> RemoveRepoAsync(
        Guid id, Guid repoId, IWorkspaceRepository repo, ITenantContext tenant, CancellationToken ct)
    {
        var removed = await repo.RemoveRepoForTenantAsync(tenant.TenantId, repoId, ct).ConfigureAwait(false);
        return removed ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> ContextAsync(
        Guid id,
        IWorkspaceRepository repo,
        ISourceProviderResolver providers,
        IAppConfigStore credentials,
        ITenantContext tenant,
        CancellationToken ct)
    {
        var row = await repo.GetAsync(id, ct).ConfigureAwait(false);
        if (row is null)
        {
            return Results.NotFound();
        }
        if (!providers.TryResolve(row.Kind, out var provider) || provider is null)
        {
            return Results.BadRequest($"No source provider registered for '{row.Kind}'.");
        }

        var repos = await repo.ListReposForTenantAsync(tenant.TenantId, id, ct).ConfigureAwait(false);
        if (repos.Count == 0)
        {
            return Results.BadRequest("This board has no repositories connected yet.");
        }
        var first = repos[0];

        var token = await credentials.GetAsync(row.CredentialRef, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(token))
        {
            return Results.BadRequest("Stored credentials for this board are missing; reconnect it.");
        }

        var descriptor = new WorkspaceDescriptor(
            row.Id, tenant.TenantId, row.Kind, first.Owner, first.Repo,
            row.Project, first.DefaultBranch, token, null);
        var context = await provider.ReadRepoContextAsync(descriptor, ct).ConfigureAwait(false);
        return Results.Ok(context);
    }
}

/// <summary>Connect-a-board request body.</summary>
internal sealed record ConnectWorkspaceRequest(
    string Name,
    SourceProviderKind Kind,
    string ProjectOwner,
    string ProjectScope,
    int? ProjectNumber,
    string? Project,
    string? Host,
    string AccessToken);

/// <summary>Add-a-repo-under-a-board request body.</summary>
internal sealed record AddRepoRequest(string Owner, string Repo, string? DefaultBranch);

/// <summary>List-connectable-repos / list-boards request body (a token probe).</summary>
internal sealed record ListReposRequest(
    SourceProviderKind Kind,
    string AccessToken,
    string? Owner,
    string? Host);

/// <summary>Board projection returned to clients — never carries the access token or CredentialRef.</summary>
internal sealed record WorkspaceDto(
    Guid Id,
    string Name,
    SourceProviderKind Kind,
    string ProjectOwner,
    string ProjectScope,
    int? ProjectNumber,
    string? Project,
    string Status,
    DateTimeOffset CreatedAtUtc)
{
    public static WorkspaceDto From(WorkspaceEntity e) => new(
        e.Id, e.Name, e.Kind, e.ProjectOwner, e.ProjectScope, e.ProjectNumber, e.Project, e.Status, e.CreatedAtUtc);
}

/// <summary>Repo-under-a-board projection.</summary>
internal sealed record RepoDto(
    Guid Id,
    string Owner,
    string Repo,
    string DefaultBranch,
    string RemoteUrl,
    bool Private,
    DateTimeOffset AddedAtUtc)
{
    public static RepoDto From(WorkspaceRepoEntity r) => new(
        r.Id, r.Owner, r.Repo, r.DefaultBranch, r.RemoteUrl, r.Private, r.AddedAtUtc);
}
