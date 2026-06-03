// Bootstrap — the write peer of IBoardTicketService. Where that reads a board's tickets, this writes
// to the board's repos: seeding the standard label taxonomy now, creating tickets from a generated
// spec in a later slice. A thin pass-through to the resolved source provider, so the Web depends on a
// small service (mirroring how it consumes IBoardTicketService) rather than the resolver directly.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Workspaces;

namespace AgentOs.Modules.Integration;

/// <summary>Writes labels (and, later, tickets) to a connected board's repos via the resolved provider.</summary>
public interface IBoardWriteService
{
    /// <summary>Idempotently seed <paramref name="labels"/> onto the repo (create missing, skip present).</summary>
    Task<LabelSyncResult> EnsureLabelsAsync(WorkspaceDescriptor repo, IReadOnlyList<LabelSpec> labels, CancellationToken cancellationToken = default);

    /// <summary>Create <paramref name="drafts"/> as real issues on <paramref name="repo"/> and add them to <paramref name="board"/>.</summary>
    Task<IReadOnlyList<CreatedTicket>> CreateTicketsAsync(BoardDescriptor board, WorkspaceDescriptor repo, IReadOnlyList<TicketDraft> drafts, CancellationToken cancellationToken = default);
}

/// <summary><see cref="IBoardWriteService"/> backed by the resolved <see cref="ISourceProvider"/>.</summary>
internal sealed class BoardWriteService : IBoardWriteService
{
    private readonly ISourceProviderResolver _providers;

    public BoardWriteService(ISourceProviderResolver providers) => _providers = providers;

    public Task<LabelSyncResult> EnsureLabelsAsync(WorkspaceDescriptor repo, IReadOnlyList<LabelSpec> labels, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repo);
        return Resolve(repo.Kind).EnsureLabelsAsync(repo, labels, cancellationToken);
    }

    public Task<IReadOnlyList<CreatedTicket>> CreateTicketsAsync(BoardDescriptor board, WorkspaceDescriptor repo, IReadOnlyList<TicketDraft> drafts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(board);
        return Resolve(board.Kind).CreateTicketsAsync(board, repo, drafts, cancellationToken);
    }

    private ISourceProvider Resolve(SourceProviderKind kind)
        => _providers.TryResolve(kind, out var provider) && provider is not null
            ? provider
            : throw new InvalidOperationException($"No source provider registered for '{kind}'.");
}
