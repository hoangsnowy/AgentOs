// M2 — Azure DevOps implementation of the source-provider seam, via the official Azure DevOps
// .NET client SDK (Microsoft.TeamFoundationServer.Client + Microsoft.VisualStudio.Services.Client):
// VssConnection authenticated with a PAT (VssBasicCredential) -> GitHttpClient. Per-workspace
// credentials come from the WorkspaceDescriptor (resolved just-in-time from the encrypted store),
// NOT from tenant-global overrides. Read-only for M2 (validate / list / read-context); PR creation
// folds in at M5.
//
// ADO URL shape: the VssConnection points at the organization
//   {host}/{org}            (host defaults to https://dev.azure.com, override for on-prem TFS)
// and Git calls are scoped by (project, repositoryId).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Workspaces;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace AgentOs.Modules.Integration.Sources;

/// <summary><see cref="ISourceProvider"/> backed by Azure DevOps via the official client SDK.</summary>
public sealed class AzureDevOpsSourceProvider : ISourceProvider
{
    private const string DefaultHost = "https://dev.azure.com";
    private const string BranchRefPrefix = "refs/heads/";

    public SourceProviderKind Kind => SourceProviderKind.AzureDevOps;

    public async Task<RepoValidation> ValidateAsync(WorkspaceDescriptor workspace, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (string.IsNullOrWhiteSpace(workspace.Project))
        {
            return RepoValidation.Fail("Azure DevOps requires a project. Supply the project name when connecting.");
        }

        try
        {
            using var connection = CreateConnection(workspace.Owner, workspace.AccessToken, workspace.Host);
            using var git = await connection.GetClientAsync<GitHttpClient>(cancellationToken).ConfigureAwait(false);
            var repo = await git.GetRepositoryAsync(workspace.Project, workspace.Repo, cancellationToken: cancellationToken).ConfigureAwait(false);
            return RepoValidation.Success(StripBranchRef(repo.DefaultBranch) ?? workspace.DefaultBranch);
        }
        catch (VssUnauthorizedException)
        {
            return RepoValidation.Fail("Azure DevOps rejected the token. Check the PAT has 'Code (read)' scope and isn't expired.");
        }
        catch (VssServiceException ex)
        {
            return RepoValidation.Fail($"Azure DevOps could not resolve {workspace.Owner}/{workspace.Project}/{workspace.Repo}: {ex.Message}");
        }
        catch (Exception ex)
        {
            return RepoValidation.Fail($"Azure DevOps error: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<RemoteRepo>> ListRepositoriesAsync(ConnectionCredentials credentials, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        if (string.IsNullOrWhiteSpace(credentials.Owner))
        {
            throw new ArgumentException("Azure DevOps listing requires the organization name (Owner).", nameof(credentials));
        }

        using var connection = CreateConnection(credentials.Owner, credentials.AccessToken, credentials.Host);
        using var git = await connection.GetClientAsync<GitHttpClient>(cancellationToken).ConfigureAwait(false);
        var repos = await git.GetRepositoriesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        return repos
            .Where(r => r.Project is not null)
            .Select(r => new RemoteRepo(
                Owner: credentials.Owner,
                Name: r.Name,
                FullName: string.Create(CultureInfo.InvariantCulture, $"{credentials.Owner}/{r.Project.Name}/{r.Name}"),
                DefaultBranch: StripBranchRef(r.DefaultBranch) ?? "main",
                RemoteUrl: r.WebUrl ?? r.RemoteUrl ?? string.Empty,
                Private: true, // ADO repos are private by default; the API doesn't surface a public flag here.
                Project: r.Project.Name))
            .ToList();
    }

    public async Task<RepoContext> ReadRepoContextAsync(WorkspaceDescriptor workspace, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (string.IsNullOrWhiteSpace(workspace.Project))
        {
            throw new InvalidOperationException("Azure DevOps requires a project to read repo context.");
        }

        using var connection = CreateConnection(workspace.Owner, workspace.AccessToken, workspace.Host);
        using var git = await connection.GetClientAsync<GitHttpClient>(cancellationToken).ConfigureAwait(false);

        var repo = await git.GetRepositoryAsync(workspace.Project, workspace.Repo, cancellationToken: cancellationToken).ConfigureAwait(false);
        var defaultBranch = StripBranchRef(repo.DefaultBranch) ?? workspace.DefaultBranch;

        var readme = await TryReadReadmeAsync(git, workspace.Project, workspace.Repo, cancellationToken).ConfigureAwait(false);
        var topLevel = await TryListTopLevelAsync(git, workspace.Project, workspace.Repo, cancellationToken).ConfigureAwait(false);

        return new RepoContext(
            FullName: string.Create(CultureInfo.InvariantCulture, $"{workspace.Owner}/{workspace.Project}/{repo.Name}"),
            DefaultBranch: defaultBranch,
            Description: null, // GitRepository carries no description field in this API.
            Readme: readme,
            TopLevelPaths: topLevel);
    }

    private static async Task<string> TryReadReadmeAsync(GitHttpClient git, string project, string repo, CancellationToken ct)
    {
        try
        {
            var item = await git.GetItemAsync(
                project: project,
                repositoryId: repo,
                path: "/README.md",
                includeContent: true,
                cancellationToken: ct).ConfigureAwait(false);
            return item?.Content ?? string.Empty;
        }
        catch (VssServiceException)
        {
            return string.Empty; // No README at the repo root.
        }
    }

    private static async Task<IReadOnlyList<string>> TryListTopLevelAsync(GitHttpClient git, string project, string repo, CancellationToken ct)
    {
        try
        {
            var items = await git.GetItemsAsync(
                project: project,
                repositoryId: repo,
                scopePath: "/",
                recursionLevel: VersionControlRecursionType.OneLevel,
                cancellationToken: ct).ConfigureAwait(false);

            return items
                .Select(i => i.Path?.TrimStart('/') ?? string.Empty)
                .Where(p => p.Length > 0)
                .ToList();
        }
        catch (VssServiceException)
        {
            return Array.Empty<string>(); // Empty repo.
        }
    }

    private static VssConnection CreateConnection(string organization, string pat, string? host)
    {
        var baseHost = string.IsNullOrWhiteSpace(host) ? DefaultHost : host!.TrimEnd('/');
        var orgUri = new Uri(string.Create(CultureInfo.InvariantCulture, $"{baseHost}/{organization}"));
        var credentials = new VssBasicCredential(string.Empty, pat);
        return new VssConnection(orgUri, credentials);
    }

    private static string? StripBranchRef(string? branchRef)
    {
        if (string.IsNullOrWhiteSpace(branchRef))
        {
            return null;
        }
        return branchRef.StartsWith(BranchRefPrefix, StringComparison.Ordinal)
            ? branchRef[BranchRefPrefix.Length..]
            : branchRef;
    }
}
