// AgenticSdlc.Api/Endpoints/PipelineEndpoints.cs
// Phase 4 — Minimal API endpoints for the 5 agents + pipeline.
// Phase 8 — Adds /pipeline/stream (SSE) so the Web can call this API for realtime runs.

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Application.Agents;
using AgenticSdlc.Application.Persistence;
using AgenticSdlc.Application.Pipeline;
using AgenticSdlc.Domain.Code;
using AgenticSdlc.Domain.Pipeline;
using AgenticSdlc.Domain.Qa;
using AgenticSdlc.Domain.Requirements;
using AgenticSdlc.Domain.Testing;

namespace AgenticSdlc.Api.Endpoints;

/// <summary>Maps endpoints for the 4 specialists + pipeline (KC1-KC4).</summary>
public static class PipelineEndpoints
{
    /// <summary>Maps endpoints onto <paramref name="app"/>.</summary>
    public static Microsoft.AspNetCore.Builder.WebApplication MapPipelineEndpoints(this Microsoft.AspNetCore.Builder.WebApplication app)
    {
        System.ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/requirement", async (UserStory body, IRequirementAgent agent, CancellationToken ct) =>
        {
            var spec = await agent.RunAsync(body, ct).ConfigureAwait(false);
            return Microsoft.AspNetCore.Http.Results.Ok(spec);
        })
        .WithName("Requirement")
        .WithSummary("KC1 — Analyze user story → structured requirement spec")
        .WithTags("Agents")
        .RequireAuthorization();

        app.MapPost("/code", async (CodeRequest body, ICodingAgent agent, CancellationToken ct) =>
        {
            var artifact = await agent.RunAsync(body.Spec, body.PreviousFeedback, ct).ConfigureAwait(false);
            return Microsoft.AspNetCore.Http.Results.Ok(artifact);
        })
        .WithName("Code")
        .WithSummary("KC2 — Generate C# Clean Architecture source code from the spec")
        .WithTags("Agents")
        .RequireAuthorization();

        app.MapPost("/test", async (TestRequest body, ITestingAgent agent, CancellationToken ct) =>
        {
            var artifact = await agent.RunAsync(body.Spec, body.Code, body.PreviousFeedback, ct).ConfigureAwait(false);
            return Microsoft.AspNetCore.Http.Results.Ok(artifact);
        })
        .WithName("Test")
        .WithSummary("KC3 — Generate xUnit tests (happy/edge/error)")
        .WithTags("Agents")
        .RequireAuthorization();

        app.MapPost("/qa", async (QaRequest body, IQaAgent agent, CancellationToken ct) =>
        {
            var report = await agent.RunAsync(body.Spec, body.Code, body.Tests, ct).ConfigureAwait(false);
            return Microsoft.AspNetCore.Http.Results.Ok(report);
        })
        .WithName("Qa")
        .WithSummary("KC5 — Assess requirement-code-test consistency")
        .WithTags("Agents")
        .RequireAuthorization();

        app.MapPost("/pipeline", async (UserStory body, IOrchestratorAgent orchestrator, CancellationToken ct) =>
        {
            var result = await orchestrator.RunAsync(body, ct).ConfigureAwait(false);
            var statusCode = result.Status == PipelineStatus.Failed ? 500 : 200;
            return Microsoft.AspNetCore.Http.Results.Json(result, statusCode: statusCode);
        })
        .WithName("Pipeline")
        .WithSummary("KC4 — End-to-end pipeline with QA loop (≤ NMax iterations)")
        .WithTags("Agents")
        .RequireAuthorization();

        // Phase 8 — Server-Sent Events stream of progress + final result.
        // Wire shape per event:   event: progress|result|error \n data: {json}\n\n
        app.MapPost("/pipeline/stream", async (UserStory body, IPipelineClient client, HttpResponse response, CancellationToken ct) =>
        {
            response.Headers.Append("Content-Type", "text/event-stream");
            response.Headers.Append("Cache-Control", "no-cache");
            response.Headers.Append("Connection", "keep-alive");
            response.Headers.Append("X-Accel-Buffering", "no"); // disable nginx buffering

            var jsonOpts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            await foreach (var evt in client.StreamAsync(body, ct).ConfigureAwait(false))
            {
                var (kind, payloadJson) = evt.Kind switch
                {
                    PipelineStreamEventKind.Progress => ("progress", JsonSerializer.Serialize(evt.Progress, jsonOpts)),
                    PipelineStreamEventKind.Result   => ("result",   JsonSerializer.Serialize(evt.Result,   jsonOpts)),
                    PipelineStreamEventKind.Error    => ("error",    JsonSerializer.Serialize(evt.Error,    jsonOpts)),
                    _ => ("unknown", "null"),
                };
                await response.WriteAsync($"event: {kind}\ndata: {payloadJson}\n\n", ct).ConfigureAwait(false);
                await response.Body.FlushAsync(ct).ConfigureAwait(false);
            }
        })
        .WithName("PipelineStream")
        .WithSummary("KC4 — Streamed pipeline run. SSE events: progress | result | error")
        .WithTags("Agents")
        .RequireAuthorization();

        app.MapGet("/runs", async (IPipelineRunRepository repo, CancellationToken ct) =>
        {
            var runs = await repo.ListAsync(50, ct).ConfigureAwait(false);
            return Microsoft.AspNetCore.Http.Results.Ok(runs);
        })
        .WithName("Runs")
        .WithSummary("Most recent pipeline run history (summary)")
        .WithTags("History")
        .RequireAuthorization();

        app.MapGet("/runs/{id:guid}", async (Guid id, IPipelineRunRepository repo, CancellationToken ct) =>
        {
            var run = await repo.GetAsync(id, ct).ConfigureAwait(false);
            return run is null
                ? Microsoft.AspNetCore.Http.Results.NotFound()
                : Microsoft.AspNetCore.Http.Results.Ok(run);
        })
        .WithName("RunById")
        .WithSummary("Details of a single pipeline run (full artifact + metrics)")
        .WithTags("History")
        .RequireAuthorization();

        return app;
    }
}

/// <summary>Body for <c>POST /code</c>.</summary>
public sealed record CodeRequest(RequirementSpec Spec, QaReport? PreviousFeedback = null);

/// <summary>Body for <c>POST /test</c>.</summary>
public sealed record TestRequest(RequirementSpec Spec, CodeArtifact Code, QaReport? PreviousFeedback = null);

/// <summary>Body for <c>POST /qa</c>.</summary>
public sealed record QaRequest(RequirementSpec Spec, CodeArtifact Code, TestArtifact Tests);
