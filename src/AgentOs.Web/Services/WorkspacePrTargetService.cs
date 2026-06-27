// Hydrates a Board-connected workspace repo into a WorkspaceDescriptor (repo coords + decrypted PAT) so the
// Pipeline can open a PR against the SAME boards the Board app manages — one repo/token source. This lives Web-side
// (not in Integration) because hydration needs IWorkspaceRepository + IAppConfigStore, which Integration must
// not reference; it extracts the descriptor build BoardApp inlines at RunSession. Side-effect-free ctor
// (eager @inject rule): it stores the two repos only — no LLM client, no I/O.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Workspaces;
using AgentOs.Modules.AppConfig;
using AgentOs.Modules.Workspaces.Persistence;

namespace AgentOs.Web.Services;

/// <summary>Resolves a connected workspace repo + its per-board PAT into a ready-to-use <see cref="WorkspaceDescriptor"/>.</summary>
public sealed class WorkspacePrTargetService
{
    private readonly IWorkspaceRepository _workspaces;
    private readonly IAppConfigStore _config;

    public WorkspacePrTargetService(IWorkspaceRepository workspaces, IAppConfigStore config)
    {
        _workspaces = workspaces ?? throw new ArgumentNullException(nameof(workspaces));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>Build a descriptor for one repo under a board, decrypting the board PAT. Returns null if the
    /// board/repo isn't found for the tenant or the PAT can't be resolved (caller falls back to the Settings token).</summary>
    public async Task<WorkspaceDescriptor?> HydrateRepoAsync(string tenantId, Guid workspaceId, Guid repoId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return null;
        }

        var board = await _workspaces.GetForTenantAsync(tenantId, workspaceId, ct).ConfigureAwait(false);
        if (board is null)
        {
            return null;
        }

        var repos = await _workspaces.ListReposForTenantAsync(tenantId, workspaceId, ct).ConfigureAwait(false);
        var repo = repos.FirstOrDefault(r => r.Id == repoId);
        if (repo is null)
        {
            return null;
        }

        var pat = await _config.GetForTenantAsync(tenantId, board.CredentialRef, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(pat))
        {
            return null;
        }

        return new WorkspaceDescriptor(
            board.Id, tenantId, board.Kind, repo.Owner, repo.Repo, board.Project, repo.DefaultBranch, pat);
    }
}
