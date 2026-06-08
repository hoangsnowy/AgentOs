// AgentOs.Web/Orchestrations/OrchestrationStore.cs
// Phase 7b → Persistence: orchestration store (singleton, in-memory cache for fast synchronous reads),
// persisted via IOrchestrationRepository (Postgres). Seeds 2 graphs if the DB is empty.
// Writes use Task.Run for deadlock-safe sync-over-async under the Blazor circuit's SynchronizationContext.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Modules.Pipeline.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace AgentOs.Web.Orchestrations;

/// <summary>Orchestration CRUD. Thread-safe enough for a single-user demo (coarse lock). Persisted via the repo (DB).</summary>
public sealed class OrchestrationStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly object _gate = new();
    // Keyed by (tenant, graph id). This store is a process-wide SINGLETON shared across every circuit, so a
    // tenant dimension is mandatory — without it one tenant's editor sees (and overwrites) another's graphs.
    // Loaded + seeded lazily per tenant on first access.
    private readonly Dictionary<(string Tenant, string Id), OrchestrationGraph> _graphs = new();
    private readonly HashSet<string> _loadedTenants = new(StringComparer.Ordinal);
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Construct. The DB load is LAZY (first access), NOT in the ctor — this store is a
    /// singleton resolved during host build / circuit open, so a blocking DB load here would stall
    /// startup and risk crashing the Blazor circuit (eager-DI). See <see cref="EnsureLoaded"/>.</summary>
    public OrchestrationStore(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        _scopeFactory = scopeFactory;
    }

    /// <summary>All orchestrations for a tenant, sorted by name.</summary>
    public IReadOnlyList<OrchestrationGraph> All(string tenantId)
    {
        EnsureLoaded(tenantId);
        lock (_gate)
        {
            return _graphs.Where(kv => kv.Key.Tenant == tenantId)
                .Select(kv => kv.Value)
                .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    /// <summary>Get by id within a tenant, null if not found.</summary>
    public OrchestrationGraph? Get(string tenantId, string id)
    {
        EnsureLoaded(tenantId);
        lock (_gate)
        {
            return _graphs.GetValueOrDefault((tenantId, id));
        }
    }

    /// <summary>Save (insert or update) into the tenant's cache + DB.</summary>
    public void Save(string tenantId, OrchestrationGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        EnsureLoaded(tenantId);
        lock (_gate)
        {
            _graphs[(tenantId, graph.Id)] = graph;
        }
        Persist(tenantId, graph);
    }

    /// <summary>Create an empty orchestration with a single Start node, owned by the tenant.</summary>
    public OrchestrationGraph Create(string tenantId, string name = "New Orchestration")
    {
        var g = new OrchestrationGraph
        {
            Id = NewId(),
            Name = name,
            Description = "Orchestration description…",
            Nodes =
            [
                new GraphNode { Id = NewId(), Type = StepType.Agent, Title = "Start", X = 80, Y = 200, IsStart = true, Description = "Entry point", Output = "result", MaxIterations = 1 },
            ],
        };
        Save(tenantId, g);
        return g;
    }

    /// <summary>Duplicate an orchestration within a tenant (name + " (copy)").</summary>
    public OrchestrationGraph Duplicate(string tenantId, string id)
    {
        EnsureLoaded(tenantId);
        OrchestrationGraph clone;
        lock (_gate)
        {
            var src = _graphs.GetValueOrDefault((tenantId, id)) ?? throw new InvalidOperationException($"Orchestration '{id}' does not exist.");
            clone = Clone(src);
            clone.Id = NewId();
            clone.Name = src.Name + " (copy)";
            _graphs[(tenantId, clone.Id)] = clone;
        }
        Persist(tenantId, clone);
        return clone;
    }

    /// <summary>Delete by id within a tenant (cache + DB).</summary>
    public void Delete(string tenantId, string id)
    {
        EnsureLoaded(tenantId);
        bool removed;
        lock (_gate)
        {
            removed = _graphs.Remove((tenantId, id));
        }
        if (removed)
        {
            RunOnRepo(repo => repo.DeleteForTenantAsync(tenantId, id));
        }
    }

    /// <summary>Short id (8 hex chars).</summary>
    public static string NewId() => Guid.NewGuid().ToString("N")[..8];

    // ---------------- persistence (repo/DB) ----------------

    // Lazily load a tenant's graphs from the DB on first access (NOT in the ctor — see the ctor remark).
    // Once per tenant, guarded by _gate; the blocking DB call runs on a Task.Run threadpool thread (see
    // RunOnRepo), never deadlocks. lock is reentrant, so callers that also take _gate are fine.
    private void EnsureLoaded(string tenantId)
    {
        lock (_gate)
        {
            if (_loadedTenants.Contains(tenantId))
            {
                return;
            }
            LoadOrSeed(tenantId);
            _loadedTenants.Add(tenantId);
        }
    }

    private void LoadOrSeed(string tenantId)
    {
        var records = RunOnRepo(repo => repo.ListForTenantAsync(tenantId));
        if (records.Count > 0)
        {
            foreach (var r in records)
            {
                var g = JsonSerializer.Deserialize<OrchestrationGraph>(r.DefinitionJson, Json);
                if (g is not null)
                {
                    _graphs[(tenantId, g.Id)] = g;
                }
            }
            return;
        }

        foreach (var g in SeedDefaults())
        {
            // The orchestration Id is the GLOBAL primary key, so the fixed seed ids (a single-bucket
            // artifact) would collide when a second tenant seeds — give each tenant's seeds fresh unique ids.
            g.Id = NewId();
            _graphs[(tenantId, g.Id)] = g;
            Persist(tenantId, g);
        }
    }

    private void Persist(string tenantId, OrchestrationGraph g)
    {
        var record = new OrchestrationRecord(
            g.Id, g.Name, g.Description, JsonSerializer.Serialize(g, Json), DateTimeOffset.UtcNow);
        RunOnRepo(repo => repo.UpsertForTenantAsync(tenantId, record));
    }

    // Deadlock-safe sync-over-async: Task.Run escapes the Blazor circuit's SynchronizationContext (avoids deadlock).
    private void RunOnRepo(Func<IOrchestrationRepository, Task> action) =>
        Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IOrchestrationRepository>();
            await action(repo).ConfigureAwait(false);
        }).GetAwaiter().GetResult();

    private IReadOnlyList<OrchestrationRecord> RunOnRepo(Func<IOrchestrationRepository, Task<IReadOnlyList<OrchestrationRecord>>> action) =>
        Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IOrchestrationRepository>();
            return await action(repo).ConfigureAwait(false);
        }).GetAwaiter().GetResult();

    private static OrchestrationGraph Clone(OrchestrationGraph src)
        => JsonSerializer.Deserialize<OrchestrationGraph>(JsonSerializer.Serialize(src, Json), Json)!;

    // ---------------- seed ----------------

    private static IEnumerable<OrchestrationGraph> SeedDefaults()
    {
        yield return SeedSdlcPipeline();
        yield return SeedStrictDeveloper();
    }

    /// <summary>Graph mapping the 5-agent pipeline — "Run" actually executes.</summary>
    private static OrchestrationGraph SeedSdlcPipeline()
    {
        string req = "req", cod = "cod", tst = "tst", qa = "qa", agg = "agg";
        return new OrchestrationGraph
        {
            Id = "sdlc-5agent",
            Name = "5-Agent SDLC Pipeline",
            Description = "Leader–Specialists–Quality Loop. Real LLM agents.",
            StateSchemaJson = "{\n  \"userStory\": \"string\",\n  \"spec\": \"RequirementSpec\",\n  \"code\": \"CodeArtifact\",\n  \"tests\": \"TestArtifact\",\n  \"qa\": \"QaReport\"\n}",
            Guardrails = ["QA score ≥ 0.8 to pass", "At most NMax iterations", "Each agent's output must match the JSON schema"],
            Nodes =
            [
                new GraphNode { Id = req, Type = StepType.Agent, AgentRole = "Requirement", Title = "Requirement Agent", X = 60, Y = 220, IsStart = true,
                    Description = "Analyze user story → spec", Input = "userStory", Output = "spec", MaxIterations = 1 },
                new GraphNode { Id = cod, Type = StepType.Agent, AgentRole = "Coding", Title = "Coding Agent", X = 340, Y = 220,
                    Description = "Generate C# source code (Clean Arch)", Input = "spec, qa", Output = "code", MaxIterations = 3 },
                new GraphNode { Id = tst, Type = StepType.Agent, AgentRole = "Testing", Title = "Testing Agent", X = 620, Y = 220,
                    Description = "Generate xUnit tests (happy/edge/error)", Input = "spec, code", Output = "tests", MaxIterations = 3 },
                new GraphNode { Id = qa, Type = StepType.Evaluator, Title = "QA Agent", X = 900, Y = 220,
                    Description = "Evaluate req-code-test consistency", Input = "spec, code, tests", Output = "qa", MaxIterations = 3,
                    Routes = ["pass", "fail"] },
                new GraphNode { Id = agg, Type = StepType.End, Title = "Aggregate", X = 1180, Y = 120,
                    Description = "Finalize result + total cost", Input = "all", Output = "result" },
            ],
            Edges =
            [
                new GraphEdge { Id = "e1", SourceId = req, TargetId = cod, Label = "spec" },
                new GraphEdge { Id = "e2", SourceId = cod, TargetId = tst, Label = "code" },
                new GraphEdge { Id = "e3", SourceId = tst, TargetId = qa, Label = "tests" },
                new GraphEdge { Id = "e4", SourceId = qa, TargetId = agg, Label = "pass" },
                new GraphEdge { Id = "e5", SourceId = qa, TargetId = cod, Label = "fail · regenerate" },
            ],
        };
    }

    /// <summary>Recreates the "Strict Developer" graph from the Synapse screenshot (just to demo the look).</summary>
    private static OrchestrationGraph SeedStrictDeveloper()
    {
        string plan = "n_plan", ev1 = "n_ev1", aplan = "n_aplan", appr = "n_appr", ev2 = "n_ev2",
            dev = "n_dev", ev3 = "n_ev3", adev = "n_adev", ev4 = "n_ev4", review = "n_review",
            ev5 = "n_ev5", git = "n_git", llm = "n_llm";
        return new OrchestrationGraph
        {
            Id = "strict-developer",
            Name = "Strict Developer",
            Description = "Always keep human in the loop to continue",
            Guardrails = ["Human approves the plan before coding", "Review code before committing"],
            Nodes =
            [
                new GraphNode { Id = plan, Type = StepType.Agent, AgentRole = "Coding", Title = "Development plan", X = 380, Y = 470, IsStart = true,
                    Description = "Code Planner", Input = "plan, plan_review_evaluation, user_answers", Output = "plan", MaxIterations = 5 },
                new GraphNode { Id = ev1, Type = StepType.Evaluator, Title = "Evaluator Step", X = 660, Y = 470,
                    Description = "Evaluate plan", Input = "plan", Output = "check_plan", MaxIterations = 5, Routes = ["ask_user", "continue"] },
                new GraphNode { Id = aplan, Type = StepType.Human, Title = "Answer Planner", X = 840, Y = 320,
                    Description = "Answer the questions", Input = "plan, check_plan", Output = "user_answers", MaxIterations = 3 },
                new GraphNode { Id = appr, Type = StepType.Human, Title = "Approve Plan", X = 380, Y = 690,
                    Description = "Analyse the plan and approve it", Input = "plan", Output = "user_plan_result", MaxIterations = 3 },
                new GraphNode { Id = ev2, Type = StepType.Evaluator, Title = "Evaluator Step", X = 640, Y = 720,
                    Description = "2 routes", Input = "user_plan_result", Output = "plan_review_evaluation", MaxIterations = 3, Routes = ["developer", "optimise_plan"] },
                new GraphNode { Id = dev, Type = StepType.Agent, AgentRole = "Coding", Title = "Developer", X = 880, Y = 560,
                    Description = "Code Executer", Input = "plan, user_analysis", Output = "development_result", MaxIterations = 10 },
                new GraphNode { Id = ev3, Type = StepType.Evaluator, Title = "Evaluator Step", X = 1100, Y = 560,
                    Description = "2 routes", Input = "development_result", Output = "code_result_analyser", MaxIterations = 3, Routes = ["ask_question_developer", "continue"] },
                new GraphNode { Id = adev, Type = StepType.Human, Title = "Answer Developer", X = 1130, Y = 380,
                    Description = "Answer the Developer's Question", Input = "development_result", Output = "user_answers_developer", MaxIterations = 3 },
                new GraphNode { Id = ev4, Type = StepType.Evaluator, Title = "Evaluator Step", X = 1130, Y = 760,
                    Description = "2 routes", Input = "plan, development_result", Output = "development_analyser_result", MaxIterations = 3, Routes = ["yes", "no"] },
                new GraphNode { Id = review, Type = StepType.Human, Title = "Review Code", X = 1380, Y = 600,
                    Description = "Review the development and give analysis", Input = "development_result", Output = "user_analysis", MaxIterations = 3 },
                new GraphNode { Id = ev5, Type = StepType.Evaluator, Title = "Evaluator Step", X = 1330, Y = 330,
                    Description = "3 routes", Input = "user_analysis", Output = "review_result", MaxIterations = 3, Routes = ["pull_request", "developer", "complete"] },
                new GraphNode { Id = git, Type = StepType.Agent, AgentRole = "Orchestrator", Title = "Git commit and pr", X = 1580, Y = 360,
                    Description = "Git Agent", Input = "development_result", Output = "git_result", MaxIterations = 3 },
                new GraphNode { Id = llm, Type = StepType.Llm, Title = "Llm Step", X = 1620, Y = 540,
                    Description = "Summarise the whole process", Input = "plan, development_result, git_result", Output = "final_summary", MaxIterations = 2 },
            ],
            Edges =
            [
                new GraphEdge { Id = "s1", SourceId = plan, TargetId = ev1, Label = "continue" },
                new GraphEdge { Id = "s2", SourceId = ev1, TargetId = aplan, Label = "ask_user" },
                new GraphEdge { Id = "s3", SourceId = ev1, TargetId = appr, Label = "continue" },
                new GraphEdge { Id = "s4", SourceId = aplan, TargetId = plan, Label = "ask_user" },
                new GraphEdge { Id = "s5", SourceId = appr, TargetId = ev2, Label = "" },
                new GraphEdge { Id = "s6", SourceId = ev2, TargetId = dev, Label = "developer" },
                new GraphEdge { Id = "s7", SourceId = ev2, TargetId = plan, Label = "optimise_plan" },
                new GraphEdge { Id = "s8", SourceId = dev, TargetId = ev3, Label = "developer" },
                new GraphEdge { Id = "s9", SourceId = ev3, TargetId = adev, Label = "ask_question_developer" },
                new GraphEdge { Id = "s10", SourceId = adev, TargetId = dev, Label = "ask_question_developer" },
                new GraphEdge { Id = "s11", SourceId = ev3, TargetId = ev4, Label = "continue" },
                new GraphEdge { Id = "s12", SourceId = ev4, TargetId = review, Label = "yes" },
                new GraphEdge { Id = "s13", SourceId = ev4, TargetId = dev, Label = "no" },
                new GraphEdge { Id = "s14", SourceId = review, TargetId = ev5, Label = "" },
                new GraphEdge { Id = "s15", SourceId = ev5, TargetId = git, Label = "pull_request" },
                new GraphEdge { Id = "s16", SourceId = ev5, TargetId = dev, Label = "developer" },
                new GraphEdge { Id = "s17", SourceId = git, TargetId = llm, Label = "" },
            ],
        };
    }
}
