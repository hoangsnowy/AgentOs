// M2 — Azure DevOps source provider. The abstraction is wired (the resolver knows ADO exists and
// the desktop can offer it), but the live implementation is deferred: it needs the Azure DevOps
// client SDK, which is a heavier dependency added in a later milestone. Until then this fails
// gracefully and honestly rather than pretending to work — Validate returns a clear failure so the
// connect flow shows a clean message; the read/list calls throw NotSupported (unreachable in
// practice, since a workspace can't be connected until Validate passes).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Workspaces;

namespace AgentOs.Modules.Integration.Sources;

/// <summary><see cref="ISourceProvider"/> for Azure DevOps — abstraction wired, live impl deferred.</summary>
public sealed class AzureDevOpsSourceProvider : ISourceProvider
{
    internal const string NotImplementedMessage =
        "Azure DevOps support is not yet implemented (the provider is registered but its live client is a later milestone). Use GitHub for now.";

    public SourceProviderKind Kind => SourceProviderKind.AzureDevOps;

    public Task<RepoValidation> ValidateAsync(WorkspaceDescriptor workspace, CancellationToken cancellationToken = default)
        => Task.FromResult(RepoValidation.Fail(NotImplementedMessage));

    public Task<IReadOnlyList<RemoteRepo>> ListRepositoriesAsync(ConnectionCredentials credentials, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(NotImplementedMessage);

    public Task<RepoContext> ReadRepoContextAsync(WorkspaceDescriptor workspace, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(NotImplementedMessage);

    // ── Boards — same deferred posture as the repo members: validate fails cleanly, reads throw. ──

    public Task<IReadOnlyList<BoardSummary>> ListBoardsAsync(ConnectionCredentials credentials, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(NotImplementedMessage);

    public Task<BoardValidation> ValidateBoardAsync(BoardDescriptor board, CancellationToken cancellationToken = default)
        => Task.FromResult(BoardValidation.Fail(NotImplementedMessage));

    public Task<BoardTickets> ReadBoardTicketsAsync(BoardDescriptor board, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(NotImplementedMessage);
}
