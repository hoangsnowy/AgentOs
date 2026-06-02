// M5 — issue-work agent. Runs the agentic LLM loop (using runner_shell) for a GitHub issue:
// explores the repo, implements the fix, builds, tests, commits, and pushes a branch.
// "Server thinks, runner does" — the LLM loop runs server-side; every shell command is
// dispatched to the paired dev-machine runner via IToolGateway → runner_shell.

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Llm;
using AgentOs.Domain.Sessions;
using AgentOs.Modules.Pipeline.Prompts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentOs.Modules.Pipeline.Agents;

/// <summary>Implements a GitHub issue fix via an agentic tool-use loop on the paired runner.</summary>
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

        _logger.LogInformation(
            "IssueWorkAgent: starting session {SessionId} for issue #{IssueNumber} on {Owner}/{Repo}",
            request.SessionId, request.IssueNumber, request.WorkspaceOwner, request.WorkspaceRepo);

        var req = new LlmRequest(
            SystemPrompt: IssueWorkPrompt.System(request),
            UserPrompt: IssueWorkPrompt.User(request),
            Model: _agentOpts.Model,
            Temperature: _agentOpts.Temperature,
            MaxTokens: _agentOpts.MaxTokens,
            Tools: ["runner_shell"]);

        LlmResponse response;
        try
        {
            var llm = _factory.Create(_agentOpts.Provider);
            response = await llm.SendAsync(req, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IssueWorkAgent: LLM call failed for session {SessionId}", request.SessionId);
            return new IssueWorkResult(false, "", "", $"LLM call failed: {ex.Message}");
        }

        return ParseResponse(response.Content ?? "", request.SessionId);
    }

    private IssueWorkResult ParseResponse(string content, Guid sessionId)
    {
        // Locate the last JSON object in the response (the agent may emit reasoning before the JSON).
        var start = content.LastIndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            _logger.LogWarning("IssueWorkAgent: no JSON found in response for session {SessionId}", sessionId);
            return new IssueWorkResult(false, "", content.Length > 200 ? content[..200] : content,
                "Agent did not return a JSON summary.");
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
                _logger.LogWarning("IssueWorkAgent: agent reported failure for session {SessionId}: {Error}",
                    sessionId, error);
                return new IssueWorkResult(false, branch, summary, error ?? "Agent did not push a branch.");
            }

            _logger.LogInformation("IssueWorkAgent: session {SessionId} pushed branch '{Branch}'", sessionId, branch);
            return new IssueWorkResult(true, branch, summary, null);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "IssueWorkAgent: failed to parse JSON for session {SessionId}", sessionId);
            return new IssueWorkResult(false, "", "", "Agent response was not valid JSON.");
        }
    }
}
