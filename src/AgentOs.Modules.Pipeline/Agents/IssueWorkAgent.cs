// M5 / multi-repo — issue-work agent. Runs the agentic LLM loop (using runner_shell) for a ticket,
// once PER target repo: explore the repo, implement the fix, build, test, commit, push a branch.
// "Server thinks, runner does" — the LLM loop runs server-side; every shell command is dispatched to
// the paired dev-machine runner via IToolGateway → runner_shell. Repos are handled sequentially
// (smallest-viable: one bounded LLM run each, reusing the proven single-repo prompt + parser).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Llm;
using AgentOs.Domain.Sessions;
using AgentOs.Modules.Pipeline.Prompts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentOs.Modules.Pipeline.Agents;

/// <summary>Implements a ticket fix via an agentic tool-use loop on the paired runner, per target repo.</summary>
public sealed class IssueWorkAgent : IIssueWorkAgent
{
    private readonly ILlmClientFactory _factory;
    private readonly AgentOptions _agentOpts;
    private readonly ILogger<IssueWorkAgent> _logger;

    public IssueWorkAgent(
        ILlmClientFactory factory,
        IOptions<AgentsOptions> options,
        ILogger<IssueWorkAgent> logger)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(options);
        // Resolve the LLM client lazily in RunAsync — NOT here. This agent is injected into the Spine
        // desktop app, so it is constructed eagerly when that window opens. Resolving a provider client
        // in the ctor builds a pooled client (which throws when no API key is configured), and that
        // exception would crash the Blazor circuit on open. The ctor must stay side-effect-free.
        _factory = factory;
        _agentOpts = options.Value.IssueWork;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IssueWorkResult> RunAsync(IssueWorkRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Repos.Count == 0)
        {
            return new IssueWorkResult(false, Array.Empty<RepoWorkOutcome>(), "No target repositories for this session.");
        }

        _logger.LogInformation(
            "IssueWorkAgent: starting session {SessionId} for ticket #{IssueNumber} across {RepoCount} repo(s)",
            request.SessionId, request.IssueNumber, request.Repos.Count);

        ILlmClient llm;
        try
        {
            llm = _factory.Create(_agentOpts.Provider);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IssueWorkAgent: LLM client init failed for session {SessionId}", request.SessionId);
            // No repo could run — report each as failed so the caller can surface it per repo.
            var failed = request.Repos
                .Select(r => new RepoWorkOutcome(r.SessionRepoId, false, r.Owner, r.Repo, "", "", $"LLM client init failed: {ex.Message}"))
                .ToList();
            return new IssueWorkResult(false, failed, $"LLM client init failed: {ex.Message}");
        }

        var outcomes = new List<RepoWorkOutcome>(request.Repos.Count);
        foreach (var repo in request.Repos)
        {
            outcomes.Add(await RunOneAsync(llm, request, repo, ct).ConfigureAwait(false));
        }

        var ok = outcomes.All(o => o.Ok);
        return new IssueWorkResult(ok, outcomes, ok ? null : "One or more repositories failed — see per-repo errors.");
    }

    private async Task<RepoWorkOutcome> RunOneAsync(ILlmClient llm, IssueWorkRequest request, WorkRepo repo, CancellationToken ct)
    {
        var req = new LlmRequest(
            SystemPrompt: IssueWorkPrompt.System(request, repo),
            UserPrompt: IssueWorkPrompt.User(request, repo),
            Model: _agentOpts.Model,
            Temperature: _agentOpts.Temperature,
            MaxTokens: _agentOpts.MaxTokens,
            Tools: ["runner_shell"]);

        try
        {
            var response = await llm.SendAsync(req, ct).ConfigureAwait(false);
            return ParseOutcome(response.Content ?? "", request, repo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IssueWorkAgent: LLM call failed for session {SessionId} repo {Owner}/{Repo}",
                request.SessionId, repo.Owner, repo.Repo);
            return new RepoWorkOutcome(repo.SessionRepoId, false, repo.Owner, repo.Repo, "", "", $"LLM call failed: {ex.Message}");
        }
    }

    private RepoWorkOutcome ParseOutcome(string content, IssueWorkRequest request, WorkRepo repo)
    {
        // Locate the last JSON object in the response (the agent may emit reasoning before the JSON).
        var start = content.LastIndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            _logger.LogWarning("IssueWorkAgent: no JSON found for session {SessionId} repo {Owner}/{Repo}",
                request.SessionId, repo.Owner, repo.Repo);
            return new RepoWorkOutcome(repo.SessionRepoId, false, repo.Owner, repo.Repo, "",
                content.Length > 200 ? content[..200] : content, "Agent did not return a JSON summary.");
        }

        var json = content[start..(end + 1)];
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var branch = root.TryGetProperty("branch", out var bProp) ? bProp.GetString() ?? "" : "";
            var summary = root.TryGetProperty("summary", out var sProp) ? sProp.GetString() ?? "" : "";
            var error = root.TryGetProperty("error", out var eProp) ? eProp.GetString() : null;

            if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(branch))
            {
                _logger.LogWarning("IssueWorkAgent: agent reported failure for session {SessionId} repo {Owner}/{Repo}: {Error}",
                    request.SessionId, repo.Owner, repo.Repo, error);
                return new RepoWorkOutcome(repo.SessionRepoId, false, repo.Owner, repo.Repo, branch, summary,
                    error ?? "Agent did not push a branch.");
            }

            _logger.LogInformation("IssueWorkAgent: session {SessionId} repo {Owner}/{Repo} pushed branch '{Branch}'",
                request.SessionId, repo.Owner, repo.Repo, branch);
            return new RepoWorkOutcome(repo.SessionRepoId, true, repo.Owner, repo.Repo, branch, summary, null);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "IssueWorkAgent: failed to parse JSON for session {SessionId} repo {Owner}/{Repo}",
                request.SessionId, repo.Owner, repo.Repo);
            return new RepoWorkOutcome(repo.SessionRepoId, false, repo.Owner, repo.Repo, "", "", "Agent response was not valid JSON.");
        }
    }
}
