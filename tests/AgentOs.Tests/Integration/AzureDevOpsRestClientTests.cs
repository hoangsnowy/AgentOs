// The Azure DevOps repo-list parser (pure, no network) + default-branch ref stripping. The live HTTP
// calls (ListRepositories / Validate) are exercised against a real org by the user, like GitHub.

using AgentOs.Modules.Integration.Sources;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Integration;

public sealed class AzureDevOpsRestClientTests
{
    private const string Sample = """
    {
      "count": 2,
      "value": [
        { "name": "api",  "defaultBranch": "refs/heads/main",    "remoteUrl": "https://dev.azure.com/acme/Platform/_git/api",  "project": { "name": "Platform" } },
        { "name": "web",  "defaultBranch": "refs/heads/develop", "remoteUrl": "https://dev.azure.com/acme/Web/_git/web",       "project": { "name": "Web" } }
      ]
    }
    """;

    [Fact]
    public void ParseRepositories_MapsNameBranchProjectAndStripsRef()
    {
        var repos = AzureDevOpsRestClient.ParseRepositories("acme", Sample);

        repos.Count.ShouldBe(2);
        repos[0].Name.ShouldBe("api");
        repos[0].Owner.ShouldBe("acme");
        repos[0].Project.ShouldBe("Platform");
        repos[0].DefaultBranch.ShouldBe("main");          // refs/heads/ stripped
        repos[0].FullName.ShouldBe("acme/Platform/api");
        repos[1].DefaultBranch.ShouldBe("develop");
    }

    [Fact]
    public void ParseRepositories_NoValueArray_ReturnsEmpty()
    {
        AzureDevOpsRestClient.ParseRepositories("acme", """{ "count": 0 }""").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("refs/heads/main", "main")]
    [InlineData("refs/heads/feature/x", "feature/x")]
    [InlineData("main", "main")]
    [InlineData(null, null)]
    public void StripRef_RemovesRefsHeadsPrefixOnly(string? input, string? expected)
    {
        AzureDevOpsRestClient.StripRef(input).ShouldBe(expected);
    }
}
