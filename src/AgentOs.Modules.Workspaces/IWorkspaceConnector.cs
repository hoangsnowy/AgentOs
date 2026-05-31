// M2 — the single connect-a-workspace flow, shared by the HTTP endpoint (tenant from ITenantContext)
// and the desktop Spine app (tenant from the signed-in principal — a circuit has no HttpContext). It
// validates the repo via the source provider, stores the access token ONLY in the encrypted AppConfig
// store under the workspace's CredentialRef, and persists the row (which never carries the secret).

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Workspaces;
using AgentOs.Modules.AppConfig;
using AgentOs.Modules.Workspaces.Persistence;
using AgentOs.Modules.Workspaces.Persistence.Entities;

namespace AgentOs.Modules.Workspaces;

/// <summary>Connect-a-workspace input. <see cref="AccessToken"/> is validated, stored encrypted, and
/// never persisted on the row or returned.</summary>
public sealed record WorkspaceConnectInput(
    string Name,
    SourceProviderKind Kind,
    string Owner,
    string Repo,
    string? Project,
    string? DefaultBranch,
    string? Host,
    string AccessToken);

/// <summary>Outcome of a connect attempt. On failure <see cref="Error"/> is a user-facing message.</summary>
public sealed record WorkspaceConnectResult(bool Ok, WorkspaceEntity? Workspace, string? Error)
{
    public static WorkspaceConnectResult Fail(string error) => new(false, null, error);
    public static WorkspaceConnectResult Success(WorkspaceEntity workspace) => new(true, workspace, null);
}

/// <summary>Validates + persists a connected workspace for an explicit tenant.</summary>
public interface IWorkspaceConnector
{
    Task<WorkspaceConnectResult> ConnectAsync(
        string tenantId, string? userId, WorkspaceConnectInput input, CancellationToken ct = default);
}

internal sealed class WorkspaceConnector : IWorkspaceConnector
{
    private readonly IWorkspaceRepository _repo;
    private readonly ISourceProviderResolver _providers;
    private readonly IAppConfigStore _credentials;
    private readonly TimeProvider _clock;

    public WorkspaceConnector(
        IWorkspaceRepository repo, ISourceProviderResolver providers, IAppConfigStore credentials, TimeProvider clock)
    {
        _repo = repo;
        _providers = providers;
        _credentials = credentials;
        _clock = clock;
    }

    public async Task<WorkspaceConnectResult> ConnectAsync(
        string tenantId, string? userId, WorkspaceConnectInput input, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (input is null || string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.AccessToken))
        {
            return WorkspaceConnectResult.Fail("Name and access token are required.");
        }
        if (!_providers.TryResolve(input.Kind, out var provider) || provider is null)
        {
            return WorkspaceConnectResult.Fail($"No source provider registered for '{input.Kind}'.");
        }

        var id = Guid.NewGuid();
        var requestedBranch = string.IsNullOrWhiteSpace(input.DefaultBranch) ? "main" : input.DefaultBranch!;
        var descriptor = new WorkspaceDescriptor(
            id, tenantId, input.Kind, input.Owner, input.Repo, input.Project, requestedBranch, input.AccessToken, input.Host);
        try
        {
            descriptor.Validate();
        }
        catch (ArgumentException ex)
        {
            return WorkspaceConnectResult.Fail(ex.Message);
        }

        var validation = await provider.ValidateAsync(descriptor, ct).ConfigureAwait(false);
        if (!validation.Ok)
        {
            return WorkspaceConnectResult.Fail(
                validation.Error ?? "Could not reach the repository with the supplied credentials.");
        }

        var credentialRef = CredentialKey(id);
        await _credentials.SetForTenantAsync(tenantId, credentialRef, input.AccessToken, ct).ConfigureAwait(false);

        var entity = new WorkspaceEntity
        {
            Id = id,
            TenantId = tenantId,
            Name = input.Name,
            Kind = input.Kind,
            Owner = input.Owner,
            Repo = input.Repo,
            Project = input.Project,
            DefaultBranch = validation.DefaultBranch ?? requestedBranch,
            RemoteUrl = BuildRemoteUrl(input.Kind, input.Owner, input.Project, input.Repo, input.Host),
            CredentialRef = credentialRef,
            CreatedByUserId = userId,
            CreatedAtUtc = _clock.GetUtcNow(),
            Status = "Connected",
        };
        await _repo.AddForTenantAsync(entity, ct).ConfigureAwait(false);
        return WorkspaceConnectResult.Success(entity);
    }

    internal static string CredentialKey(Guid id) =>
        string.Create(CultureInfo.InvariantCulture, $"workspace/{id:N}/token");

    internal static string BuildRemoteUrl(SourceProviderKind kind, string owner, string? project, string repo, string? host)
    {
        var baseHost = string.IsNullOrWhiteSpace(host)
            ? (kind == SourceProviderKind.AzureDevOps ? "https://dev.azure.com" : "https://github.com")
            : host!.TrimEnd('/');

        return kind == SourceProviderKind.AzureDevOps
            ? string.Create(CultureInfo.InvariantCulture, $"{baseHost}/{owner}/{project}/_git/{repo}")
            : string.Create(CultureInfo.InvariantCulture, $"{baseHost}/{owner}/{repo}");
    }
}
