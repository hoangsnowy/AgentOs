// Epic E4 — Exposes the AgentOs pipeline as MCP tools so any MCP host (Claude Desktop, VS Code,
// custom orchestrators) can drive a run without going through REST. The DI container injects
// IPipelineClient + IPipelineRunRepository per call; the underlying ITenantContext / auth still
// runs because the MCP endpoint sits behind ASP.NET's standard middleware.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Pipeline;
using AgentOs.Domain.Requirements;
using AgentOs.Modules.Pipeline.Persistence;
using AgentOs.Modules.Pipeline.Pipeline;
using ModelContextProtocol.Server;

namespace AgentOs.Api.Mcp;

[McpServerToolType]
public static class PipelineMcpTools
{
    [McpServerTool(Name = "run_pipeline"), Description(
        "Runs the AgentOs 5-agent pipeline (Requirement -> Coding -> Testing -> QA -> Orchestrator) "
        + "on a single user story and returns the final PipelineResult (spec + code + tests + QA history + total metrics). "
        + "Use this when you want AgentOs to produce reviewed, test-backed code from a plain-English story.")]
    public static async Task<PipelineResult?> RunPipelineAsync(
        IPipelineClient client,
        [Description("The user story / requirement, free-form text. Required.")] string description,
        [Description("Max QA loop iterations. 1..10. Default 3.")] int? maxIterations,
        [Description("Output locale, e.g. en-US, vi-VN. Default vi-VN.")] string? locale,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("description is required.", nameof(description));
        }

        var story = new UserStory(
            Description: description,
            NMax: Math.Clamp(maxIterations ?? 3, 1, 10),
            Locale: string.IsNullOrWhiteSpace(locale) ? "vi-VN" : locale);

        PipelineResult? result = null;
        await foreach (var ev in client.StreamAsync(story, cancellationToken).ConfigureAwait(false))
        {
            if (ev.Result is not null)
            {
                result = ev.Result;
            }
        }
        return result;
    }

    [McpServerTool(Name = "list_runs"), Description(
        "Lists recent pipeline runs for the calling tenant. Returns metadata only "
        + "(id, status, started, finished, total cost, user-story preview) — not the full artifacts.")]
    public static Task<IReadOnlyList<PipelineRunSummary>> ListRunsAsync(
        IPipelineRunRepository repository,
        [Description("Maximum runs to return. Default 20, max 100.")] int? limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);
        var take = Math.Clamp(limit ?? 20, 1, 100);
        return repository.ListAsync(take, offset: 0, cancellationToken);
    }

    [McpServerTool(Name = "get_run"), Description(
        "Fetches the full stored PipelineRunRecord (PipelineResult + per-call metrics) for a run id, "
        + "or null when no such run exists for the calling tenant.")]
    public static Task<PipelineRunRecord?> GetRunAsync(
        IPipelineRunRepository repository,
        [Description("Run id (GUID). Required.")] string runId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (!Guid.TryParse(runId, out var id))
        {
            throw new ArgumentException("runId must be a valid GUID.", nameof(runId));
        }
        return repository.GetAsync(id, cancellationToken);
    }
}
