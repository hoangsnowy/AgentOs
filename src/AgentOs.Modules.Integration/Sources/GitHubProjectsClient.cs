// Board reshape — GitHub Projects v2 access over GraphQL. Octokit's REST surface (used by
// GitHubSourceProvider for repos/issues/PRs) does NOT cover Projects v2, so this talks raw GraphQL
// to {host}/graphql with System.Text.Json — no extra package, full control over the query shape.
// A board's items span many repos; ReadItemsAsync projects each item's content (Issue/PullRequest/
// DraftIssue) + the board's single-select Status field onto a provider-neutral BoardTicket.
//
// One shared HttpClient with per-request bearer auth (the recommended singleton pattern); tokens are
// transient and never logged. The JSON→BoardTicket mapping is exposed internally so it can be unit
// tested against canned GraphQL responses without a live GitHub.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Workspaces;

namespace AgentOs.Modules.Integration.Sources;

/// <summary>Reads GitHub Projects v2 boards + their items via GraphQL.</summary>
internal static class GitHubProjectsClient
{
    private const string UserAgent = "agentos";

    // Cap total items pulled for one board — mirrors the 50-issue philosophy of the old per-repo feed
    // while allowing a few pages for a real board.
    private const int MaxItems = 200;

    private static readonly HttpClient Http = new();

    private const string UserBoardsQuery = """
        query($login:String!){ user(login:$login){ projectsV2(first:50){ nodes{ id number title } } } }
        """;

    private const string OrgBoardsQuery = """
        query($login:String!){ organization(login:$login){ projectsV2(first:50){ nodes{ id number title } } } }
        """;

    private const string UserBoardQuery = """
        query($login:String!,$number:Int!){ user(login:$login){ projectV2(number:$number){ id title } } }
        """;

    private const string OrgBoardQuery = """
        query($login:String!,$number:Int!){ organization(login:$login){ projectV2(number:$number){ id title } } }
        """;

    private const string ItemsQuery = """
        query($projectId:ID!,$after:String){
          node(id:$projectId){
            ... on ProjectV2 {
              items(first:50, after:$after){
                pageInfo{ hasNextPage endCursor }
                nodes{
                  id
                  fieldValueByName(name:"Status"){ ... on ProjectV2ItemFieldSingleSelectValue { name } }
                  content{
                    __typename
                    ... on Issue       { number title state url repository{ name owner{ login } } labels(first:10){ nodes{ name } } assignees(first:1){ nodes{ login } } }
                    ... on PullRequest { number title state url repository{ name owner{ login } } labels(first:10){ nodes{ name } } assignees(first:1){ nodes{ login } } }
                    ... on DraftIssue  { title }
                  }
                }
              }
            }
          }
        }
        """;

    /// <summary>List the Projects v2 boards owned by <paramref name="owner"/> under the given scope.</summary>
    public static async Task<IReadOnlyList<BoardSummary>> ListBoardsAsync(
        string owner, string scope, string token, string? host, CancellationToken ct)
    {
        var query = scope == "org" ? OrgBoardsQuery : UserBoardsQuery;
        var data = await PostAsync(query, new { login = owner }, token, host, ct).ConfigureAwait(false);

        if (!data.TryGetProperty(scope == "org" ? "organization" : "user", out var root)
            || root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("projectsV2", out var pv2)
            || !pv2.TryGetProperty("nodes", out var nodes))
        {
            return Array.Empty<BoardSummary>();
        }

        var list = new List<BoardSummary>();
        foreach (var n in nodes.EnumerateArray())
        {
            list.Add(new BoardSummary(
                Number: n.TryGetProperty("number", out var num) ? num.GetInt32() : 0,
                NodeId: n.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                Title: n.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                Scope: scope,
                OwnerLogin: owner));
        }
        return list;
    }

    /// <summary>Resolve a board by number → its node id + title.</summary>
    public static async Task<BoardValidation> ValidateBoardAsync(BoardDescriptor board, CancellationToken ct)
    {
        if (board.ProjectNumber is null)
        {
            return BoardValidation.Fail("No board number — attach a GitHub Projects v2 board number to read its tickets.");
        }

        try
        {
            var query = board.ProjectScope == "org" ? OrgBoardQuery : UserBoardQuery;
            var data = await PostAsync(
                query, new { login = board.ProjectOwner, number = board.ProjectNumber.Value },
                board.AccessToken, board.Host, ct).ConfigureAwait(false);

            if (!data.TryGetProperty(board.ProjectScope == "org" ? "organization" : "user", out var ownerEl)
                || ownerEl.ValueKind != JsonValueKind.Object)
            {
                return BoardValidation.Fail($"No {board.ProjectScope} '{board.ProjectOwner}' is visible to this token.");
            }
            if (!ownerEl.TryGetProperty("projectV2", out var proj) || proj.ValueKind != JsonValueKind.Object)
            {
                return BoardValidation.Fail($"Board #{board.ProjectNumber} was not found for {board.ProjectOwner}.");
            }
            return BoardValidation.Success(
                proj.TryGetProperty("id", out var pid) ? pid.GetString() ?? "" : "",
                proj.TryGetProperty("title", out var pt) ? pt.GetString() ?? "" : "");
        }
        catch (InvalidOperationException ex)
        {
            return BoardValidation.Fail(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            return BoardValidation.Fail($"GitHub was unreachable: {ex.Message}");
        }
    }

    /// <summary>Read all items on the board (paginated, capped), projected onto <see cref="BoardTicket"/>.</summary>
    public static async Task<BoardTickets> ReadItemsAsync(BoardDescriptor board, CancellationToken ct)
    {
        var nodeId = board.ProjectNodeId;
        if (string.IsNullOrEmpty(nodeId))
        {
            var v = await ValidateBoardAsync(board, ct).ConfigureAwait(false);
            if (!v.Ok || string.IsNullOrEmpty(v.NodeId))
            {
                throw new InvalidOperationException(v.Error ?? "Could not resolve the board.");
            }
            nodeId = v.NodeId;
        }

        var items = new List<BoardTicket>();
        string? after = null;
        do
        {
            var data = await PostAsync(
                ItemsQuery, new { projectId = nodeId, after }, board.AccessToken, board.Host, ct).ConfigureAwait(false);
            after = ParseItemsPage(data, items);
        }
        while (after is not null && items.Count < MaxItems);

        return new BoardTickets(items);
    }

    /// <summary>Parse one items page from a GraphQL <c>data</c> element into <paramref name="sink"/>; returns the next cursor or null.</summary>
    internal static string? ParseItemsPage(JsonElement data, List<BoardTicket> sink)
    {
        if (!data.TryGetProperty("node", out var node) || node.ValueKind != JsonValueKind.Object
            || !node.TryGetProperty("items", out var itemsEl))
        {
            return null;
        }

        if (itemsEl.TryGetProperty("nodes", out var nodes))
        {
            foreach (var it in nodes.EnumerateArray())
            {
                var ticket = MapItem(it);
                if (ticket is not null)
                {
                    sink.Add(ticket);
                }
                if (sink.Count >= MaxItems)
                {
                    return null;
                }
            }
        }

        if (itemsEl.TryGetProperty("pageInfo", out var pageInfo)
            && pageInfo.TryGetProperty("hasNextPage", out var hasNext) && hasNext.GetBoolean()
            && pageInfo.TryGetProperty("endCursor", out var cursor))
        {
            return cursor.GetString();
        }
        return null;
    }

    /// <summary>Parse a full GraphQL items response (the <c>data</c> object as JSON) — the unit-test seam.</summary>
    internal static IReadOnlyList<BoardTicket> ParseItemsResponse(string dataJson)
    {
        using var doc = JsonDocument.Parse(dataJson);
        var sink = new List<BoardTicket>();
        ParseItemsPage(doc.RootElement, sink);
        return sink;
    }

    /// <summary>Map one ProjectV2 item node → a provider-neutral ticket. Null for an item whose content was archived/redacted.</summary>
    internal static BoardTicket? MapItem(JsonElement item)
    {
        var itemId = item.TryGetProperty("id", out var iid) ? iid.GetString() ?? "" : "";

        string? status = null;
        if (item.TryGetProperty("fieldValueByName", out var fv) && fv.ValueKind == JsonValueKind.Object
            && fv.TryGetProperty("name", out var sn))
        {
            status = sn.GetString();
        }

        if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var typename = content.TryGetProperty("__typename", out var tn) ? tn.GetString() : null;

        if (typename == "DraftIssue")
        {
            var draftTitle = content.TryGetProperty("title", out var dt) ? dt.GetString() ?? "" : "";
            return new BoardTicket(
                itemId, null, draftTitle, string.Empty, BoardTicketKind.DraftIssue,
                null, null, string.Empty, Array.Empty<string>(), status, null);
        }

        var kind = typename == "PullRequest" ? BoardTicketKind.PullRequest : BoardTicketKind.Issue;
        var number = content.TryGetProperty("number", out var nu) ? nu.GetInt32() : (int?)null;
        var title = content.TryGetProperty("title", out var ti) ? ti.GetString() ?? "" : "";
        var state = content.TryGetProperty("state", out var st) ? st.GetString() ?? "" : "";
        var url = content.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";

        string? repoOwner = null, repoName = null;
        if (content.TryGetProperty("repository", out var repo) && repo.ValueKind == JsonValueKind.Object)
        {
            repoName = repo.TryGetProperty("name", out var rn) ? rn.GetString() : null;
            if (repo.TryGetProperty("owner", out var ow) && ow.ValueKind == JsonValueKind.Object)
            {
                repoOwner = ow.TryGetProperty("login", out var ol) ? ol.GetString() : null;
            }
        }

        var labels = new List<string>();
        if (content.TryGetProperty("labels", out var lab) && lab.TryGetProperty("nodes", out var lnodes))
        {
            foreach (var l in lnodes.EnumerateArray())
            {
                if (l.TryGetProperty("name", out var ln) && ln.GetString() is { } name)
                {
                    labels.Add(name);
                }
            }
        }

        string? assignee = null;
        if (content.TryGetProperty("assignees", out var asg) && asg.TryGetProperty("nodes", out var anodes)
            && anodes.ValueKind == JsonValueKind.Array && anodes.GetArrayLength() > 0)
        {
            assignee = anodes[0].TryGetProperty("login", out var al) ? al.GetString() : null;
        }

        return new BoardTicket(itemId, number, title, state, kind, repoOwner, repoName, url, labels, status, assignee);
    }

    private static Uri GraphQlEndpoint(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return new Uri("https://api.github.com/graphql");
        }
        return new Uri($"{host.TrimEnd('/')}/api/graphql");
    }

    /// <summary>POST a GraphQL query; returns a standalone clone of the <c>data</c> element. Throws on HTTP/GraphQL errors with a scope-aware message.</summary>
    private static async Task<JsonElement> PostAsync(string query, object variables, string token, string? host, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, GraphQlEndpoint(host));
        req.Headers.UserAgent.ParseAdd(UserAgent);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Accept.ParseAdd("application/json");
        req.Content = new StringContent(JsonSerializer.Serialize(new { query, variables }), Encoding.UTF8, "application/json");

        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (resp.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException("GitHub rejected the token. Check it has 'repo' + 'read:project' scope and isn't expired.");
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
        {
            var first = errors[0];
            var type = first.TryGetProperty("type", out var t) ? t.GetString() : null;
            var message = first.TryGetProperty("message", out var m) ? m.GetString() : null;
            throw type switch
            {
                "FORBIDDEN" => new InvalidOperationException(
                    "GitHub denied access to the board — the token needs the 'read:project' (Projects: Read) scope. " + message),
                "NOT_FOUND" => new InvalidOperationException(
                    "Board not found, or the token can't see it. " + message),
                _ => new InvalidOperationException(message ?? "GitHub GraphQL error."),
            };
        }

        if (!root.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
        {
            throw new InvalidOperationException("GitHub returned no data for the board query.");
        }
        return data.Clone();
    }
}
