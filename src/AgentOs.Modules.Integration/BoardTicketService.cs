// Board reshape — the board-shaped successor to IGitHubIssueService. Where the old service fetched
// issues from ONE repo, this reads the tickets (items) off a board, which span many repos. A thin
// pass-through to the resolved source provider's ReadBoardTicketsAsync, mirroring how the issue
// service wrapped Octokit — the provider owns the transport (GraphQL for GitHub Projects v2).

using System;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Workspaces;

namespace AgentOs.Modules.Integration;

/// <summary>Reads the tickets on a connected board (across all its repos).</summary>
public interface IBoardTicketService
{
    Task<BoardTickets> ListTicketsAsync(BoardDescriptor board, CancellationToken cancellationToken = default);
}

/// <summary><see cref="IBoardTicketService"/> backed by the resolved <see cref="ISourceProvider"/>.</summary>
internal sealed class BoardTicketService : IBoardTicketService
{
    private readonly ISourceProviderResolver _providers;

    public BoardTicketService(ISourceProviderResolver providers) => _providers = providers;

    public Task<BoardTickets> ListTicketsAsync(BoardDescriptor board, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(board);
        if (!_providers.TryResolve(board.Kind, out var provider) || provider is null)
        {
            throw new InvalidOperationException($"No source provider registered for '{board.Kind}'.");
        }
        return provider.ReadBoardTicketsAsync(board, cancellationToken);
    }
}
