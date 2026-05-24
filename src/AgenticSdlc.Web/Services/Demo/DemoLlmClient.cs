// AgenticSdlc.Web/Services/Demo/DemoLlmClient.cs
// Phase 7 — A "canned" LLM source for offline Demo mode. NO network calls: it identifies the calling
// agent from the system prompt then returns schema-valid JSON, while also simulating the Quality Loop
// (QA fails the first round → passes the next) so the defense can see the iteration mechanism.
//
// Note: this client is circuit-scoped, so the iteration counters are independent across user sessions.
// A single pipeline run is sequential (the orchestrator awaits each agent), so the counters are safe.

using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Domain.Llm;
using AgenticSdlc.Infrastructure.Llm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AgenticSdlc.Web.Services.Demo;

/// <summary>
/// An <see cref="ILlmClient"/> implementation that returns deterministic sample data for the offline demo.
/// It distinguishes agents by the opening line of the system prompt ("You are the … Agent").
/// </summary>
public sealed class DemoLlmClient : ILlmClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly int _stepDelayMs;
    private readonly int _failingQaRounds;
    private readonly ILogger<DemoLlmClient> _logger;

    // Per-circuit counters — reset when a new story begins (the Requirement call).
    private int _qaCalls;
    private int _codingCalls;

    /// <inheritdoc />
    public string Provider => "Demo";

    /// <summary>Initialize, reading the speed parameter + the number of failing QA rounds from the <c>Demo</c> section.</summary>
    public DemoLlmClient(IConfiguration configuration, ILogger<DemoLlmClient> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _stepDelayMs = configuration.GetValue("Demo:StepDelayMs", 650);
        _failingQaRounds = Math.Max(0, configuration.GetValue("Demo:FailingQaRounds", 1));
    }

    /// <inheritdoc />
    public async Task<LlmResponse> SendAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        var sw = Stopwatch.StartNew();
        if (_stepDelayMs > 0)
        {
            await Task.Delay(_stepDelayMs, cancellationToken).ConfigureAwait(false);
        }

        var sys = request.SystemPrompt ?? string.Empty;
        string content;
        int inTok, outTok;

        // Match on the IDENTIFIER LINE "You are the <X> Agent" — not a bare "<X> Agent" suffix,
        // because other prompts cross-reference agents (e.g. the Testing prompt mentions "Coding Agent"),
        // which would mis-route. This literal is a routing key shared with the Application prompt files
        // (Application/Prompts/*.cs) — keep both in sync if you reword the prompt opening line.
        if (Contains(sys, "You are the Requirement Agent"))
        {
            _qaCalls = 0;
            _codingCalls = 0;
            content = RequirementJson(request.UserPrompt);
            (inTok, outTok) = (420, 360);
        }
        else if (Contains(sys, "You are the Coding Agent"))
        {
            _codingCalls++;
            content = CodeJson(_codingCalls);
            (inTok, outTok) = (680, _codingCalls > 1 ? 1240 : 1180);
        }
        else if (Contains(sys, "You are the Testing Agent"))
        {
            content = TestJson();
            (inTok, outTok) = (640, 760);
        }
        else if (Contains(sys, "You are the QA Agent"))
        {
            _qaCalls++;
            var pass = _qaCalls > _failingQaRounds;
            content = QaJson(pass);
            (inTok, outTok) = (880, pass ? 240 : 320);
        }
        else
        {
            content = "{}";
            (inTok, outTok) = (10, 2);
            _logger.LogWarning("DemoLlmClient: could not identify the agent from the system prompt.");
        }

        sw.Stop();
        var cost = CostCalculator.Calculate(request.Model, inTok, outTok);
        return new LlmResponse(content, inTok, outTok, cost, sw.Elapsed, request.Model, Provider);
    }

    private static bool Contains(string s, string marker)
        => s.Contains(marker, StringComparison.OrdinalIgnoreCase);

    // ---------------- Canned payloads ----------------

    private static string RequirementJson(string userPrompt)
    {
        var snippet = Snippet(userPrompt, 140);
        var obj = new
        {
            title = "Manage products in the catalog",
            summary = $"Allows administrators to create, update, and search products with a unique SKU. (User story: {snippet})",
            stakeholders = new[] { "Administrator", "Customer" },
            functionalRequirements = new[]
            {
                "Create a new product with a unique SKU",
                "Update product information and price",
                "Soft-delete a product",
                "Search products by name with pagination",
            },
            nonFunctionalRequirements = new[]
            {
                "p95 response time ≤ 200ms",
                "Log every data-write operation",
            },
            entities = new[]
            {
                new
                {
                    name = "Product",
                    fields = new[] { "Id: Guid", "Sku: string", "Name: string", "Price: decimal", "IsActive: bool" },
                    notes = "SKU must be unique across the entire system",
                },
            },
            endpoints = new[]
            {
                new { method = "POST", path = "/products", purpose = "Create a new product", authRequired = true },
                new { method = "GET", path = "/products/{id}", purpose = "Get product details", authRequired = false },
                new { method = "GET", path = "/products", purpose = "List + search with pagination", authRequired = false },
            },
            acceptanceCriteria = new[]
            {
                "Creating a product with a duplicate SKU must return 409 Conflict",
                "Fetching a non-existent product returns 404 Not Found",
                "The list supports pagination with a max pageSize of 100",
            },
        };
        return JsonSerializer.Serialize(obj, Json);
    }

    private static string CodeJson(int iteration)
    {
        var fixedNote = iteration > 1
            ? "Regeneration round: added pagination to GET /products and a duplicate-SKU check per QA feedback."
            : "First generation following Clean Architecture (Domain / Application / Api).";

        var obj = new
        {
            projectName = "ProductCatalog",
            architecture = "Clean Architecture",
            files = new[]
            {
                new
                {
                    path = "src/Domain/Product.cs",
                    content =
                        "namespace ProductCatalog.Domain;\n\n" +
                        "public sealed class Product\n{\n" +
                        "    public Guid Id { get; init; } = Guid.NewGuid();\n" +
                        "    public required string Sku { get; init; }\n" +
                        "    public required string Name { get; set; }\n" +
                        "    public decimal Price { get; set; }\n" +
                        "    public bool IsActive { get; set; } = true;\n}\n",
                    language = "csharp",
                },
                new
                {
                    path = "src/Application/IProductRepository.cs",
                    content =
                        "namespace ProductCatalog.Application;\n\n" +
                        "public interface IProductRepository\n{\n" +
                        "    Task<bool> SkuExistsAsync(string sku, CancellationToken ct);\n" +
                        "    Task AddAsync(Domain.Product product, CancellationToken ct);\n" +
                        "    Task<IReadOnlyList<Domain.Product>> SearchAsync(string? q, int page, int size, CancellationToken ct);\n}\n",
                    language = "csharp",
                },
                new
                {
                    path = "src/Api/ProductEndpoints.cs",
                    content =
                        "namespace ProductCatalog.Api;\n\n" +
                        "public static class ProductEndpoints\n{\n" +
                        "    public static void MapProducts(this IEndpointRouteBuilder app)\n    {\n" +
                        "        app.MapPost(\"/products\", async (CreateProduct cmd, IProductRepository repo, CancellationToken ct) =>\n" +
                        "        {\n" +
                        "            if (await repo.SkuExistsAsync(cmd.Sku, ct))\n" +
                        "                return Results.Conflict($\"SKU {cmd.Sku} already exists.\");\n" +
                        "            var p = new Domain.Product { Sku = cmd.Sku, Name = cmd.Name, Price = cmd.Price };\n" +
                        "            await repo.AddAsync(p, ct);\n" +
                        "            return Results.Created($\"/products/{p.Id}\", p);\n" +
                        "        });\n    }\n}\n\n" +
                        "public sealed record CreateProduct(string Sku, string Name, decimal Price);\n",
                    language = "csharp",
                },
            },
            notes = fixedNote,
        };
        return JsonSerializer.Serialize(obj, Json);
    }

    private static string TestJson()
    {
        var obj = new
        {
            framework = "xUnit",
            files = new[]
            {
                new
                {
                    path = "tests/ProductEndpointsTests.cs",
                    content =
                        "using Shouldly;\nusing Xunit;\n\n" +
                        "public class ProductEndpointsTests\n{\n" +
                        "    [Fact]\n" +
                        "    public async Task CreateProduct_NewSku_Returns201()\n    {\n" +
                        "        var repo = new FakeRepo(skuExists: false);\n" +
                        "        var result = await CreateHandler.Run(new(\"SKU-1\", \"Table\", 100m), repo);\n" +
                        "        result.ShouldBeOfType<Created<Product>>();\n    }\n\n" +
                        "    [Fact]\n" +
                        "    public async Task CreateProduct_DuplicateSku_Returns409()\n    {\n" +
                        "        var repo = new FakeRepo(skuExists: true);\n" +
                        "        var result = await CreateHandler.Run(new(\"SKU-1\", \"Table\", 100m), repo);\n" +
                        "        result.ShouldBeOfType<Conflict<string>>();\n    }\n\n" +
                        "    [Theory]\n    [InlineData(-1)]\n    [InlineData(0)]\n" +
                        "    public async Task CreateProduct_InvalidPrice_Throws(decimal price)\n    {\n" +
                        "        var repo = new FakeRepo(skuExists: false);\n" +
                        "        await Should.ThrowAsync<ArgumentException>(\n" +
                        "            () => CreateHandler.Run(new(\"SKU-2\", \"Chair\", price), repo));\n    }\n}\n",
                    language = "csharp",
                },
            },
            happyPathCount = 1,
            edgeCaseCount = 2,
            errorCaseCount = 1,
            estimatedCoveragePercent = 78,
        };
        return JsonSerializer.Serialize(obj, Json);
    }

    private static string QaJson(bool pass)
    {
        object obj = pass
            ? new
            {
                score = 0.92,
                isConsistent = true,
                iterationNeeded = false,
                issues = new[]
                {
                    new
                    {
                        severity = "Minor",
                        category = "CodeQuality",
                        description = "Consider adding FluentValidation for CreateProduct (optional).",
                        location = "src/Api/ProductEndpoints.cs",
                    },
                },
                recommendations = Array.Empty<string>(),
            }
            : new
            {
                score = 0.62,
                isConsistent = false,
                iterationNeeded = true,
                issues = new[]
                {
                    new
                    {
                        severity = "Major",
                        category = "RequirementCoverage",
                        description = "The GET /products endpoint does not implement pagination per the acceptance criteria.",
                        location = "src/Api/ProductEndpoints.cs",
                    },
                    new
                    {
                        severity = "Major",
                        category = "TestCoverage",
                        description = "Missing tests for pagination and the pageSize ≤ 100 limit.",
                        location = "tests/ProductEndpointsTests.cs",
                    },
                },
                recommendations = new[]
                {
                    "Add page/size parameters to GET /products and clamp size ≤ 100.",
                    "Add pagination tests to ProductEndpointsTests.",
                },
            };
        return JsonSerializer.Serialize(obj, Json);
    }

    private static string Snippet(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "(none)";
        }
        var t = text.Trim().ReplaceLineEndings(" ");
        return t.Length <= max ? t : string.Concat(t.AsSpan(0, max), "…");
    }
}
