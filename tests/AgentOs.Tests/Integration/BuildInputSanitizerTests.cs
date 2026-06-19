// ADR-0005 Layer 1 — the input chokepoint that closes the build_verifier RCE. The model may
// contribute SOURCE files only; every project/solution/import/config/response file is classified
// as build-control and dropped before any `dotnet build` runs. VerifyFilesAsync just calls this and
// `continue`s, so this classifier IS the security decision and is tested exhaustively here.

using AgentOs.Modules.Integration;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Integration;

public sealed class BuildInputSanitizerTests
{
    [Theory]
    // Project + solution files
    [InlineData("Evil.csproj")]
    [InlineData("Evil.vbproj")]
    [InlineData("Evil.fsproj")]
    [InlineData("build.proj")]
    [InlineData("App.sln")]
    [InlineData("App.slnx")]
    // MSBuild imports (auto-evaluated)
    [InlineData("Custom.targets")]
    [InlineData("Custom.props")]
    [InlineData("Directory.Build.props")]
    [InlineData("Directory.Build.targets")]
    [InlineData("Directory.Packages.props")]
    // NuGet / SDK / response files
    [InlineData("nuget.config")]
    [InlineData("NuGet.Config")]          // case-insensitive
    [InlineData("packages.config")]
    [InlineData("global.json")]
    [InlineData("Directory.Build.rsp")]
    [InlineData("MSBuild.rsp")]
    // nested anywhere in the tree
    [InlineData("src/inner/Evil.csproj")]
    [InlineData("a/b/c/Directory.Build.props")]
    // Windows trailing-dot / trailing-space bypass: the OS strips these on write, so the file lands as a
    // real build-control file and MSBuild auto-imports it (RCE). Must be rejected after normalisation.
    [InlineData("Directory.Build.props.")]
    [InlineData("Directory.Build.props ")]
    [InlineData("Evil.csproj.")]
    [InlineData("Evil.csproj  ")]
    [InlineData("nuget.config.")]
    [InlineData("a/b/Custom.targets. ")]
    public void IsBuildControlFile_BuildControlPaths_AreRejected(string path)
    {
        BuildInputSanitizer.IsBuildControlFile(path).ShouldBeTrue();
    }

    [Theory]
    [InlineData("Program.cs")]
    [InlineData("src/Domain/Product.cs")]
    [InlineData("Component.razor")]
    [InlineData("Page.cshtml")]
    [InlineData("appsettings.json")]       // content json is harmless; only global.json steers the build
    [InlineData("data.txt")]
    [InlineData("README.md")]
    [InlineData("styles.css")]
    [InlineData("config.xml")]             // a plain xml is not an MSBuild import
    public void IsBuildControlFile_SourceAndContentPaths_AreKept(string path)
    {
        BuildInputSanitizer.IsBuildControlFile(path).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsBuildControlFile_NullOrBlank_IsNotBuildControl(string? path)
    {
        // Blank paths are skipped by the writer separately; the classifier just reports "not control".
        BuildInputSanitizer.IsBuildControlFile(path).ShouldBeFalse();
    }
}
