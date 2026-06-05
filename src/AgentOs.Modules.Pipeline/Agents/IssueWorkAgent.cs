// M5 / multi-repo — issue-work agent. Runs the agentic LLM loop (using runner_shell) for a ticket,
// once PER target repo: explore the repo, implement the fix, build, test, commit, push a branch.
// "Server thinks, runner does" — the LLM loop runs server-side; every shell command is dispatched to
// the paired dev-machine runner via IToolGateway → runner_shell. Repos run with bounded concurrency
// (Agents:IssueWork:MaxParallelRepos, default 3) — one LLM run each, reusing the proven single-repo
// prompt + parser; each repo has its own working_dir so concurrent runs don't collide.

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
    // A single agentic CLI run on the member's machine (clone → build → test → push) far outlasts the
    // RemoteAgent default 120s dispatch cap, so CLI-mode runs get a generous deadline.
    private static readonly TimeSpan CliRunTimeout = TimeSpan.FromMinutes(20);

    private readonly ILlmClientFactory _factory;
    private readonly AgentOptions _agentOpts;
    private readonly ILogger<IssueWorkAgent> _logger;
    private readonly ISessionRunFeed? _feed;

    public IssueWorkAgent(
        ILlmClientFactory factory,
        IOptions<AgentsOptions> options,
        ILogger<IssueWorkAgent> logger,
        ISessionRunFeed? feed = null)
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
        // Optional: the live progress feed (null in unit tests / hosts that don't register it).
        _feed = feed;
    }

    // Best-effort publish to the live session-run feed — never throws, no-op when unsubscribed.
    private void Emit(IssueWorkRequest req, SessionRunEventKind kind, string message, string? repo = null) =>
        _feed?.Publish(new SessionRunEvent(req.TenantId, req.SessionId, kind, message, DateTimeOffset.UtcNow, repo));

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

        // RemoteAgent provider = route the WHOLE agentic loop to the member's paired dev-machine CLI
        // (claude-code / codex). That CLI has its own native tools (no runner_shell), so we switch to
        // the CLI-mode prompt + a long dispatch timeout, and the run costs zero server tokens.
        var providerName = request.ProviderOverride ?? _agentOpts.Provider;
        var cliMode = providerName.Trim().ToUpperInvariant() is "REMOTEAGENT" or "REMOTE" or "IDE";

        Emit(request, SessionRunEventKind.Running, cliMode
            ? $"Running on your machine's CLI — {request.Repos.Count} repo(s), 0 server tokens."
            : $"Dispatched to runner — {request.Repos.Count} repo(s).");

        ILlmClient llm;
        try
        {
            llm = _factory.Create(providerName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IssueWorkAgent: LLM client init failed for session {SessionId}", request.SessionId);
            Emit(request, SessionRunEventKind.Step, $"LLM client init failed: {ex.Message}");
            // No repo could run — report each as failed so the caller can surface it per repo.
            var failed = request.Repos
                .Select(r => new RepoWorkOutcome(r.SessionRepoId, false, r.Owner, r.Repo, "", "", $"LLM client init failed: {ex.Message}"))
                .ToList();
            return new IssueWorkResult(false, failed, $"LLM client init failed: {ex.Message}");
        }

        // Run repos with bounded concurrency. Each repo is an independent LLM run with its own
        // working_dir on the runner, so they don't collide; the broker dispatches per-call (GUID
        // ToolCallId) so concurrent runner_shell calls are safe. A semaphore caps fan-out to keep the
        // dev machine sane; each task writes only its own slot in the outcomes array (order-stable,
        // no shared-state contention). MaxParallelRepos=1 reproduces the old sequential behaviour.
        var maxParallel = Math.Max(1, _agentOpts.MaxParallelRepos);
        var outcomes = new RepoWorkOutcome[request.Repos.Count];
        using var gate = new SemaphoreSlim(maxParallel, maxParallel);

        async Task RunRepoSlotAsync(int index)
        {
            var repo = request.Repos[index];
            var repoLabel = $"{repo.Owner}/{repo.Repo}";
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                Emit(request, SessionRunEventKind.RepoStarted, $"{repoLabel} — implementing…", repoLabel);
                var outcome = await RunOneAsync(llm, request, repo, cliMode, ct).ConfigureAwait(false);
                Emit(request, SessionRunEventKind.Step,
                    outcome.Ok
                        ? $"{repoLabel} — pushed branch {outcome.BranchName}"
                        : $"{repoLabel} — agent failed: {outcome.Error}",
                    repoLabel);
                outcomes[index] = outcome;
            }
            finally
            {
                gate.Release();
            }
        }

        await Task.WhenAll(Enumerable.Range(0, request.Repos.Count).Select(RunRepoSlotAsync)).ConfigureAwait(false);

        var ok = outcomes.All(o => o.Ok);
        return new IssueWorkResult(ok, outcomes, ok ? null : "One or more repositories failed — see per-repo errors.");
    }

    private async Task<RepoWorkOutcome> RunOneAsync(ILlmClient llm, IssueWorkRequest request, WorkRepo repo, bool cliMode, CancellationToken ct)
    {
        // Server-side (default): the LLM drives the paired runner via the runner_shell tool loop.
        // CLI mode (RemoteAgent): the dev-machine CLI has its own tools — send the CLI-mode prompt,
        // expose no server tools, and grant a long timeout for the full clone→build→test→push run.
        var req = cliMode
            ? new LlmRequest(
                SystemPrompt: IssueWorkPrompt.SystemCli(request, repo),
                UserPrompt: IssueWorkPrompt.User(request, repo),
                Model: _agentOpts.Model,
                Temperature: _agentOpts.Temperature,
                MaxTokens: _agentOpts.MaxTokens,
                Tools: [],
                Timeout: CliRunTimeout)
            : new LlmRequest(
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
