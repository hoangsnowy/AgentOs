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
using Microsoft.Extensions.Logging;

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
    private readonly ILogger<OrchestrationStore> _logger;

    /// <summary>Construct. The DB load is LAZY (first access), NOT in the ctor — this store is a
    /// singleton resolved during host build / circuit open, so a blocking DB load here would stall
    /// startup and risk crashing the Blazor circuit (eager-DI). See <see cref="EnsureLoaded"/>.</summary>
    public OrchestrationStore(IServiceScopeFactory scopeFactory, ILogger<OrchestrationStore> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(logger);
        _scopeFactory = scopeFactory;
        _logger = logger;
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
            var loaded = 0;
            foreach (var r in records)
            {
                // A single corrupt/legacy DefinitionJson row must NOT take down the whole Studio for the
                // tenant — deserialize defensively, log + skip the bad row, keep the rest. (OrchestrationGraph.Id
                // is a `required` member, so a row missing "id" throws JsonException here.)
                OrchestrationGraph? g;
                try
                {
                    g = JsonSerializer.Deserialize<OrchestrationGraph>(r.DefinitionJson, Json);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Skipping corrupt orchestration row {Id} for tenant {Tenant}", r.Id, tenantId);
                    continue;
                }

                if (g is not null)
                {
                    _graphs[(tenantId, g.Id)] = g;
                    loaded++;
                }
            }

            // At least one row materialized → done. If EVERY row was corrupt, fall through to seed defaults
            // so the tenant still gets a working Studio (the bad rows stay in the DB but are ignored).
            if (loaded > 0)
            {
                return;
            }

            _logger.LogWarning("All {Count} orchestration rows for tenant {Tenant} were corrupt; seeding defaults", records.Count, tenantId);
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
        yield return SeedControlFlowDemo();
        yield return SeedStrictDeveloper();
    }

    /// <summary>A small, fully-runnable graph that exercises EVERY control-flow node — Parallel fan-out,
    /// Merge fan-in, an If/Else branch, and a Human checkpoint — using raw LLM steps so it runs end-to-end
    /// with no API key (offline fallback). The showcase for "the drawn graph actually executes".</summary>
    private static OrchestrationGraph SeedControlFlowDemo()
    {
        string draft = "cf_draft", split = "cf_split", pros = "cf_pros", cons = "cf_cons",
            join = "cf_join", gate = "cf_gate", human = "cf_human", end = "cf_end";
        return new OrchestrationGraph
        {
            Id = "control-flow-demo",
            Name = "Control-Flow Demo",
            Description = "Parallel + Merge + If/Else + Human checkpoint. Runs end-to-end with no API key.",
            StateSchemaJson = "{\n  \"input\": \"string\",\n  \"draft\": \"string\",\n  \"decision\": \"proceed | revise\"\n}",
            Guardrails = ["Human signs off before completion", "If/Else gate decides proceed vs revise"],
            Nodes =
            [
                new GraphNode { Id = draft, Type = StepType.Llm, Title = "Draft idea", X = 60, Y = 240, IsStart = true,
                    Description = "Draft an approach for: ${userStory}", Input = "userStory", Output = "draft" },
                new GraphNode { Id = split, Type = StepType.Parallel, Title = "Fork analysis", X = 320, Y = 240,
                    Description = "Run pros + cons in parallel", Input = "draft", Output = "fork" },
                new GraphNode { Id = pros, Type = StepType.Llm, Title = "Analyze pros", X = 560, Y = 130,
                    Description = "List the strengths of: ${draft}", Input = "draft", Output = "pros" },
                new GraphNode { Id = cons, Type = StepType.Llm, Title = "Analyze cons", X = 560, Y = 360,
                    Description = "List the risks of: ${draft}", Input = "draft", Output = "cons" },
                new GraphNode { Id = join, Type = StepType.Merge, Title = "Combine", X = 820, Y = 240,
                    Description = "Merge pros + cons", Input = "pros, cons", Output = "analysis" },
                new GraphNode { Id = gate, Type = StepType.IfElse, Title = "Worth it?", X = 1060, Y = 240,
                    Description = "Given the analysis, should we proceed?", Input = "analysis", Output = "decision",
                    Routes = ["proceed", "revise"] },
                new GraphNode { Id = human, Type = StepType.Human, Title = "Operator sign-off", X = 1300, Y = 140,
                    Description = "Approve the plan before finishing.", Input = "analysis", Output = "approval", MaxIterations = 1 },
                new GraphNode { Id = end, Type = StepType.End, Title = "Done", X = 1540, Y = 240,
                    Description = "Finalize", Input = "all", Output = "result" },
            ],
            Edges =
            [
                new GraphEdge { Id = "c1", SourceId = draft, TargetId = split, Label = "" },
                new GraphEdge { Id = "c2", SourceId = split, TargetId = pros, Label = "pros" },
                new GraphEdge { Id = "c3", SourceId = split, TargetId = cons, Label = "cons" },
                new GraphEdge { Id = "c4", SourceId = pros, TargetId = join, Label = "" },
                new GraphEdge { Id = "c5", SourceId = cons, TargetId = join, Label = "" },
                new GraphEdge { Id = "c6", SourceId = join, TargetId = gate, Label = "" },
                new GraphEdge { Id = "c7", SourceId = gate, TargetId = human, Label = "proceed" },
                new GraphEdge { Id = "c8", SourceId = gate, TargetId = end, Label = "revise" },
                new GraphEdge { Id = "c9", SourceId = human, TargetId = end, Label = "" },
            ],
        };
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
