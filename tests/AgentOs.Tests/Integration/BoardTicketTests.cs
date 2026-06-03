// Board reshape — the GitHub Projects v2 GraphQL → BoardTicket mapping (the parse seam, no live
// GitHub) and the BoardTicketService pass-through to the resolved provider.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Workspaces;
using AgentOs.Modules.Integration;
using AgentOs.Modules.Integration.Sources;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Integration;

public sealed class BoardTicketTests
{
    private static readonly string[] ExpectedIssueLabels = ["bug", "p1"];

    // A GraphQL `data` payload for one items page: an Issue (with repo/labels/status/assignee), a
    // DraftIssue (no repo), a merged PullRequest, and an item whose content is null (archived) which
    // must be skipped.
    private const string ItemsData = """
        {
          "node": {
            "items": {
              "pageInfo": { "hasNextPage": false, "endCursor": null },
              "nodes": [
                {
                  "id": "ITEM_1",
                  "fieldValueByName": { "name": "In Progress" },
                  "content": {
                    "__typename": "Issue",
                    "number": 42,
                    "title": "Fix login redirect",
                    "state": "OPEN",
                    "url": "https://github.com/octo/api/issues/42",
                    "repository": { "name": "api", "owner": { "login": "octo" } },
                    "labels": { "nodes": [ { "name": "bug" }, { "name": "p1" } ] },
                    "assignees": { "nodes": [ { "login": "alice" } ] }
                  }
                },
                {
                  "id": "ITEM_2",
                  "fieldValueByName": null,
                  "content": { "__typename": "DraftIssue", "title": "Spike: caching" }
                },
                {
                  "id": "ITEM_3",
                  "fieldValueByName": { "name": "Done" },
                  "content": {
                    "__typename": "PullRequest",
                    "number": 7,
                    "title": "Add SSO",
                    "state": "MERGED",
                    "url": "https://github.com/web/ui/pull/7",
                    "repository": { "name": "ui", "owner": { "login": "web" } },
                    "labels": { "nodes": [] },
                    "assignees": { "nodes": [] }
                  }
                },
                { "id": "ITEM_4", "content": null }
              ]
            }
          }
        }
        """;

    [Fact]
    public void ParseItemsResponse_MapsIssue_WithRepoLabelsStatusAssignee()
    {
        var tickets = GitHubProjectsClient.ParseItemsResponse(ItemsData);

        var issue = tickets.Single(t => t.ItemNodeId == "ITEM_1");
        issue.Kind.ShouldBe(BoardTicketKind.Issue);
        issue.Number.ShouldBe(42);
        issue.Title.ShouldBe("Fix login redirect");
        issue.State.ShouldBe("OPEN");
        issue.RepoOwner.ShouldBe("octo");
        issue.RepoName.ShouldBe("api");
        issue.HtmlUrl.ShouldBe("https://github.com/octo/api/issues/42");
        issue.Labels.ShouldBe(ExpectedIssueLabels);
        issue.Status.ShouldBe("In Progress");
        issue.AssigneeLogin.ShouldBe("alice");
    }

    [Fact]
    public void ParseItemsResponse_MapsDraftIssue_WithNullRepoAndNumber()
    {
        var tickets = GitHubProjectsClient.ParseItemsResponse(ItemsData);

        var draft = tickets.Single(t => t.ItemNodeId == "ITEM_2");
        draft.Kind.ShouldBe(BoardTicketKind.DraftIssue);
        draft.Number.ShouldBeNull();
        draft.RepoOwner.ShouldBeNull();
        draft.RepoName.ShouldBeNull();
        draft.Status.ShouldBeNull();
        draft.Title.ShouldBe("Spike: caching");
    }

    [Fact]
    public void ParseItemsResponse_MapsPullRequest_AndSkipsContentlessItems()
    {
        var tickets = GitHubProjectsClient.ParseItemsResponse(ItemsData);

        tickets.Count.ShouldBe(3); // ITEM_4 (null content) is skipped.
        var pr = tickets.Single(t => t.ItemNodeId == "ITEM_3");
        pr.Kind.ShouldBe(BoardTicketKind.PullRequest);
        pr.Number.ShouldBe(7);
        pr.RepoOwner.ShouldBe("web");
        pr.Status.ShouldBe("Done");
        pr.Labels.ShouldBeEmpty();
        pr.AssigneeLogin.ShouldBeNull();
    }

    [Fact]
    public async Task BoardTicketService_DelegatesToResolvedProvider()
    {
        var provider = Substitute.For<ISourceProvider>();
        provider.Kind.Returns(SourceProviderKind.GitHub);
        var resolver = Substitute.For<ISourceProviderResolver>();
        resolver.TryResolve(SourceProviderKind.GitHub, out Arg.Any<ISourceProvider?>()!)
            .Returns(ci => { ci[1] = provider; return true; });

        var board = new BoardDescriptor(
            Guid.NewGuid(), "t1", SourceProviderKind.GitHub, "octo", "org", 5, "node", "tok");
        var expected = new BoardTickets(new[]
        {
            new BoardTicket("i1", 1, "T", "OPEN", BoardTicketKind.Issue, "octo", "api", "url", Array.Empty<string>(), null, null),
        });
        provider.ReadBoardTicketsAsync(board, Arg.Any<CancellationToken>()).Returns(expected);

        var svc = new BoardTicketService(resolver);
        var result = await svc.ListTicketsAsync(board);

        result.Items.Count.ShouldBe(1);
        result.Items[0].RepoName.ShouldBe("api");
    }

    [Fact]
    public async Task BoardTicketService_NoProvider_Throws()
    {
        var resolver = Substitute.For<ISourceProviderResolver>();
        resolver.TryResolve(Arg.Any<SourceProviderKind>(), out Arg.Any<ISourceProvider?>()!)
            .Returns(ci => { ci[1] = null; return false; });
        var board = new BoardDescriptor(
            Guid.NewGuid(), "t1", SourceProviderKind.AzureDevOps, "org", "org", null, null, "tok", "proj");

        var svc = new BoardTicketService(resolver);

        await Should.ThrowAsync<InvalidOperationException>(() => svc.ListTicketsAsync(board));
    }
}
