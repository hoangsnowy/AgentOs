// AgenticSdlc.Tests/Validation/JsonSchemaValidatorTests.cs
// Sprint 3 — 3 pass + 3 fail per schema = 18 tests for JSON Schema validation.

using AgenticSdlc.Modules.Pipeline.Validation;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AgenticSdlc.Tests.Validation;

public class JsonSchemaValidatorTests
{
    private static ILlmOutputValidator Build()
    {
        var sc = new ServiceCollection();
        sc.AddValidation();
        return sc.BuildServiceProvider().GetRequiredService<ILlmOutputValidator>();
    }

    // ---------- RequirementSpec — 3 PASS ----------

    [Fact]
    public void Requirement_FullValidSpec_Passes()
    {
        var json = """
            {
              "title": "Product management",
              "summary": "Product CRUD.",
              "entities": [{"name":"Product","fields":["id: Guid"]}],
              "endpoints": [{"method":"POST","path":"/products","purpose":"Create"}],
              "acceptanceCriteria": ["a","b","c"]
            }
            """;
        Build().Validate(json, SchemaNames.RequirementSpecV1, "test");
    }

    [Fact]
    public void Requirement_ManyEntitiesEndpointsAC_Passes()
    {
        var json = """
            {
              "title": "X", "summary": "Y",
              "entities": [{"name":"A","fields":[]},{"name":"B","fields":[]}],
              "endpoints": [
                {"method":"GET","path":"/a","purpose":"p","authRequired":false},
                {"method":"POST","path":"/b","purpose":"p"}
              ],
              "acceptanceCriteria": ["c1","c2","c3","c4","c5"]
            }
            """;
        Build().Validate(json, SchemaNames.RequirementSpecV1, "test");
    }

    [Fact]
    public void Requirement_WithOptionalArrays_Passes()
    {
        var json = """
            {
              "title": "T", "summary": "S",
              "stakeholders": ["admin"],
              "functionalRequirements": ["fr1"],
              "nonFunctionalRequirements": ["nfr1"],
              "entities": [{"name":"E","fields":["x: int"],"notes":"n"}],
              "endpoints": [{"method":"PUT","path":"/x","purpose":"upd","authRequired":true}],
              "acceptanceCriteria": ["a","b","c"]
            }
            """;
        Build().Validate(json, SchemaNames.RequirementSpecV1, "test");
    }

    // ---------- RequirementSpec — 3 FAIL ----------

    [Fact]
    public void Requirement_MissingEntities_Fails()
    {
        var json = """{"title":"T","summary":"S","entities":[],"endpoints":[{"method":"GET","path":"/","purpose":"p"}],"acceptanceCriteria":["a","b","c"]}""";
        var ex = Should.Throw<LlmOutputValidationException>(
            () => Build().Validate(json, SchemaNames.RequirementSpecV1, "test"));
        ex.Errors.ShouldNotBeEmpty();
        ex.Message.ShouldContain("entities");
    }

    [Fact]
    public void Requirement_InvalidHttpMethod_Fails()
    {
        var json = """{"title":"T","summary":"S","entities":[{"name":"E","fields":[]}],"endpoints":[{"method":"FETCH","path":"/x","purpose":"p"}],"acceptanceCriteria":["a","b","c"]}""";
        Should.Throw<LlmOutputValidationException>(
            () => Build().Validate(json, SchemaNames.RequirementSpecV1, "test"));
    }

    [Fact]
    public void Requirement_AcLessThanThree_Fails()
    {
        var json = """{"title":"T","summary":"S","entities":[{"name":"E","fields":[]}],"endpoints":[{"method":"GET","path":"/","purpose":"p"}],"acceptanceCriteria":["a","b"]}""";
        var ex = Should.Throw<LlmOutputValidationException>(
            () => Build().Validate(json, SchemaNames.RequirementSpecV1, "test"));
        ex.Message.ShouldContain("acceptanceCriteria");
    }

    // ---------- CodeArtifact — 3 PASS ----------

    [Fact]
    public void Code_MinimalValid_Passes()
    {
        var json = """{"projectName":"ProductCatalog","files":[{"path":"src/Program.cs","content":"// noop"}]}""";
        Build().Validate(json, SchemaNames.CodeArtifactV1, "test");
    }

    [Fact]
    public void Code_MultiFile_Passes()
    {
        var json = """{"projectName":"App","architecture":"Clean Architecture","files":[{"path":"a.cs","content":"x","language":"csharp"},{"path":"b.cs","content":"y","language":"csharp"}],"notes":"ok"}""";
        Build().Validate(json, SchemaNames.CodeArtifactV1, "test");
    }

    [Fact]
    public void Code_WithDotProjectName_Passes()
    {
        var json = """{"projectName":"My.App.Web","files":[{"path":"x","content":""}]}""";
        Build().Validate(json, SchemaNames.CodeArtifactV1, "test");
    }

    // ---------- CodeArtifact — 3 FAIL ----------

    [Fact]
    public void Code_MissingProjectName_Fails()
    {
        var json = """{"files":[{"path":"a","content":""}]}""";
        var ex = Should.Throw<LlmOutputValidationException>(
            () => Build().Validate(json, SchemaNames.CodeArtifactV1, "test"));
        ex.Message.ShouldContain("projectName");
    }

    [Fact]
    public void Code_EmptyFiles_Fails()
    {
        var json = """{"projectName":"X","files":[]}""";
        Should.Throw<LlmOutputValidationException>(
            () => Build().Validate(json, SchemaNames.CodeArtifactV1, "test"));
    }

    [Fact]
    public void Code_FileMissingContent_Fails()
    {
        var json = """{"projectName":"X","files":[{"path":"a"}]}""";
        Should.Throw<LlmOutputValidationException>(
            () => Build().Validate(json, SchemaNames.CodeArtifactV1, "test"));
    }

    // ---------- TestArtifact — 3 PASS ----------

    [Fact]
    public void Test_MinimalValid_Passes()
    {
        var json = """{"files":[{"path":"a.cs","content":"x"}],"happyPathCount":1,"edgeCaseCount":0,"errorCaseCount":0}""";
        Build().Validate(json, SchemaNames.TestArtifactV1, "test");
    }

    [Fact]
    public void Test_FullValid_Passes()
    {
        var json = """{"framework":"xUnit","files":[{"path":"a.cs","content":"x","language":"csharp"}],"happyPathCount":3,"edgeCaseCount":2,"errorCaseCount":1,"estimatedCoveragePercent":80}""";
        Build().Validate(json, SchemaNames.TestArtifactV1, "test");
    }

    [Fact]
    public void Test_ZeroCounts_Passes()
    {
        var json = """{"files":[{"path":"a","content":""}],"happyPathCount":0,"edgeCaseCount":0,"errorCaseCount":0,"estimatedCoveragePercent":0}""";
        Build().Validate(json, SchemaNames.TestArtifactV1, "test");
    }

    // ---------- TestArtifact — 3 FAIL ----------

    [Fact]
    public void Test_EmptyFiles_Fails()
    {
        var json = """{"files":[],"happyPathCount":1,"edgeCaseCount":0,"errorCaseCount":0}""";
        Should.Throw<LlmOutputValidationException>(
            () => Build().Validate(json, SchemaNames.TestArtifactV1, "test"));
    }

    [Fact]
    public void Test_MissingCounts_Fails()
    {
        var json = """{"files":[{"path":"a","content":""}]}""";
        Should.Throw<LlmOutputValidationException>(
            () => Build().Validate(json, SchemaNames.TestArtifactV1, "test"));
    }

    [Fact]
    public void Test_NegativeCount_Fails()
    {
        var json = """{"files":[{"path":"a","content":""}],"happyPathCount":-1,"edgeCaseCount":0,"errorCaseCount":0}""";
        Should.Throw<LlmOutputValidationException>(
            () => Build().Validate(json, SchemaNames.TestArtifactV1, "test"));
    }
}
