// AgenticSdlc.Web/Orchestrations/OrchestrationStore.cs
// Phase 7b → Persistence: kho orchestration (singleton, cache in-memory cho read sync nhanh),
// lưu bền qua IOrchestrationRepository (Postgres). Seed sẵn 2 đồ thị nếu DB trống.
// Write dùng Task.Run để sync-over-async an toàn deadlock dưới SynchronizationContext của Blazor circuit.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AgenticSdlc.Application.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticSdlc.Web.Orchestrations;

/// <summary>CRUD orchestration. Thread-safe đủ cho demo 1 người (lock thô). Persist qua repo (DB).</summary>
public sealed class OrchestrationStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly object _gate = new();
    private readonly Dictionary<string, OrchestrationGraph> _graphs = new(StringComparer.Ordinal);
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Khởi tạo: nạp từ DB nếu có, ngược lại seed mặc định + lưu.</summary>
    public OrchestrationStore(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        _scopeFactory = scopeFactory;
        LoadOrSeed();
    }

    /// <summary>Tất cả orchestration, sắp theo tên.</summary>
    public IReadOnlyList<OrchestrationGraph> All()
    {
        lock (_gate)
        {
            return _graphs.Values.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    /// <summary>Lấy theo id, null nếu không có.</summary>
    public OrchestrationGraph? Get(string id)
    {
        lock (_gate)
        {
            return _graphs.GetValueOrDefault(id);
        }
    }

    /// <summary>Lưu (thêm mới hoặc cập nhật) vào cache + DB.</summary>
    public void Save(OrchestrationGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        lock (_gate)
        {
            _graphs[graph.Id] = graph;
        }
        Persist(graph);
    }

    /// <summary>Tạo orchestration trống với 1 node Start.</summary>
    public OrchestrationGraph Create(string name = "New Orchestration")
    {
        var g = new OrchestrationGraph
        {
            Id = NewId(),
            Name = name,
            Description = "Mô tả orchestration…",
            Nodes =
            [
                new GraphNode { Id = NewId(), Type = StepType.Agent, Title = "Start", X = 80, Y = 200, IsStart = true, Description = "Điểm bắt đầu", Output = "result", MaxIterations = 1 },
            ],
        };
        Save(g);
        return g;
    }

    /// <summary>Nhân bản orchestration (tên + " (copy)").</summary>
    public OrchestrationGraph Duplicate(string id)
    {
        OrchestrationGraph clone;
        lock (_gate)
        {
            var src = _graphs.GetValueOrDefault(id) ?? throw new InvalidOperationException($"Orchestration '{id}' không tồn tại.");
            clone = Clone(src);
            clone.Id = NewId();
            clone.Name = src.Name + " (copy)";
            _graphs[clone.Id] = clone;
        }
        Persist(clone);
        return clone;
    }

    /// <summary>Xoá theo id (cache + DB).</summary>
    public void Delete(string id)
    {
        bool removed;
        lock (_gate)
        {
            removed = _graphs.Remove(id);
        }
        if (removed)
        {
            RunOnRepo(repo => repo.DeleteAsync(id));
        }
    }

    /// <summary>Id ngắn (8 hex).</summary>
    public static string NewId() => Guid.NewGuid().ToString("N")[..8];

    // ---------------- persistence (repo/DB) ----------------

    private void LoadOrSeed()
    {
        var records = RunOnRepo(repo => repo.ListAsync());
        if (records.Count > 0)
        {
            foreach (var r in records)
            {
                var g = JsonSerializer.Deserialize<OrchestrationGraph>(r.DefinitionJson, Json);
                if (g is not null)
                {
                    _graphs[g.Id] = g;
                }
            }
            return;
        }

        foreach (var g in SeedDefaults())
        {
            _graphs[g.Id] = g;
            Persist(g);
        }
    }

    private void Persist(OrchestrationGraph g)
    {
        var record = new OrchestrationRecord(
            g.Id, g.Name, g.Description, JsonSerializer.Serialize(g, Json), DateTimeOffset.UtcNow);
        RunOnRepo(repo => repo.UpsertAsync(record));
    }

    // Sync-over-async an toàn: Task.Run thoát SynchronizationContext của Blazor circuit (tránh deadlock).
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

    /// <summary>Đồ thị ánh xạ pipeline 5 tác tử của luận văn — "Run" chạy thật được.</summary>
    private static OrchestrationGraph SeedSdlcPipeline()
    {
        string req = "req", cod = "cod", tst = "tst", qa = "qa", agg = "agg";
        return new OrchestrationGraph
        {
            Id = "sdlc-5agent",
            Name = "5-Agent SDLC Pipeline",
            Description = "Leader–Specialists–Quality Loop (KC1–KC5). Run được bằng tác tử thật/Demo.",
            StateSchemaJson = "{\n  \"userStory\": \"string\",\n  \"spec\": \"RequirementSpec\",\n  \"code\": \"CodeArtifact\",\n  \"tests\": \"TestArtifact\",\n  \"qa\": \"QaReport\"\n}",
            Guardrails = ["QA score ≥ 0.8 mới pass", "Tối đa NMax vòng lặp", "Output mỗi agent phải hợp JSON schema"],
            Nodes =
            [
                new GraphNode { Id = req, Type = StepType.Agent, AgentRole = "Requirement", Title = "Requirement Agent", X = 60, Y = 220, IsStart = true,
                    Description = "Phân tích user story → spec", Input = "userStory", Output = "spec", MaxIterations = 1 },
                new GraphNode { Id = cod, Type = StepType.Agent, AgentRole = "Coding", Title = "Coding Agent", X = 340, Y = 220,
                    Description = "Sinh source code C# (Clean Arch)", Input = "spec, qa", Output = "code", MaxIterations = 3 },
                new GraphNode { Id = tst, Type = StepType.Agent, AgentRole = "Testing", Title = "Testing Agent", X = 620, Y = 220,
                    Description = "Sinh xUnit test (happy/edge/error)", Input = "spec, code", Output = "tests", MaxIterations = 3 },
                new GraphNode { Id = qa, Type = StepType.Evaluator, Title = "QA Agent", X = 900, Y = 220,
                    Description = "Đánh giá nhất quán req-code-test", Input = "spec, code, tests", Output = "qa", MaxIterations = 3,
                    Routes = ["pass", "fail"] },
                new GraphNode { Id = agg, Type = StepType.End, Title = "Aggregate", X = 1180, Y = 120,
                    Description = "Chốt kết quả + tổng chi phí", Input = "all", Output = "result" },
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

    /// <summary>Mô phỏng đồ thị "Strict Developer" trong ảnh Synapse (chỉ để trình diễn look).</summary>
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
            Guardrails = ["Human phê duyệt plan trước khi code", "Review code trước khi commit"],
            Nodes =
            [
                new GraphNode { Id = plan, Type = StepType.Agent, AgentRole = "Coding", Title = "Development plan", X = 380, Y = 470, IsStart = true,
                    Description = "Code Planner", Input = "plan, plan_review_evaluation, user_answers", Output = "plan", MaxIterations = 5 },
                new GraphNode { Id = ev1, Type = StepType.Evaluator, Title = "Evaluator Step", X = 660, Y = 470,
                    Description = "Đánh giá plan", Input = "plan", Output = "check_plan", MaxIterations = 5, Routes = ["ask_user", "continue"] },
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
