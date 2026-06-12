// Batch 2 — the /settings allowlist: POST /settings only accepts registered keys with valid values.
using AgentOs.Modules.AppConfig.Endpoints;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.AppConfig;

public class SettingsKeyRegistryTests
{
    [Theory]
    [InlineData("Llm:ForceProvider")]
    [InlineData("Llm:Claude:ApiKey")]
    [InlineData("Llm:AzureOpenAi:ApiKey")]
    [InlineData("Llm:AzureOpenAi:Endpoint")]
    [InlineData("Github:Pat")]
    [InlineData("Github:RepoOwner")]
    [InlineData("Github:RepoName")]
    [InlineData("Github:BaseBranch")]
    [InlineData("llm:forceprovider")] // case-insensitive
    public void IsKnownKey_AllowlistedKey_ReturnsTrue(string key) =>
        SettingsKeyRegistry.IsKnownKey(key).ShouldBeTrue();

    [Theory]
    [InlineData("Logging:LogLevel:Default")]
    [InlineData("Auth:Keycloak:ClientSecret")]
    [InlineData("tools/enforce")]
    [InlineData("ConnectionStrings:DefaultConnection")]
    [InlineData("")]
    public void IsKnownKey_ForeignKey_ReturnsFalse(string key) =>
        SettingsKeyRegistry.IsKnownKey(key).ShouldBeFalse();

    [Theory]
    [InlineData("Llm")]
    [InlineData("Github")]
    public void IsReadablePrefix_KnownPrefix_ReturnsTrue(string prefix) =>
        SettingsKeyRegistry.IsReadablePrefix(prefix).ShouldBeTrue();

    [Theory]
    [InlineData("LLM")]
    [InlineData("github")]
    public void IsReadablePrefix_CaseVariantOfKnownPrefix_ReturnsTrue(string prefix) =>
        SettingsKeyRegistry.IsReadablePrefix(prefix).ShouldBeTrue();

    [Theory]
    [InlineData("Auth")]
    [InlineData("AUTH")]
    [InlineData("tools")]
    [InlineData("ConnectionStrings")]
    public void IsReadablePrefix_ForeignPrefix_ReturnsFalse(string prefix) =>
        SettingsKeyRegistry.IsReadablePrefix(prefix).ShouldBeFalse();

    [Theory]
    [InlineData("Claude")]
    [InlineData("AzureOpenAI")]
    [InlineData("MAF")]
    [InlineData("RemoteAgent")]
    [InlineData("Anthropic")] // alias — LlmClientFactory normalizes to Claude
    [InlineData("")] // empty clears the override
    public void ValidateValue_ForceProvider_ValidValues_ReturnNull(string value) =>
        SettingsKeyRegistry.ValidateValue("Llm:ForceProvider", value).ShouldBeNull();

    [Fact]
    public void ValidateValue_ForceProvider_UnknownProvider_ReturnsError() =>
        SettingsKeyRegistry.ValidateValue("Llm:ForceProvider", "FakeAI").ShouldNotBeNull();

    [Theory]
    [InlineData("https://my-resource.openai.azure.com")]
    [InlineData("")]
    public void ValidateValue_AzureEndpoint_HttpsOrEmpty_ReturnsNull(string value) =>
        SettingsKeyRegistry.ValidateValue("Llm:AzureOpenAi:Endpoint", value).ShouldBeNull();

    [Theory]
    [InlineData("http://my-resource.openai.azure.com")] // not https
    [InlineData("not-a-url")]
    public void ValidateValue_AzureEndpoint_Invalid_ReturnsError(string value) =>
        SettingsKeyRegistry.ValidateValue("Llm:AzureOpenAi:Endpoint", value).ShouldNotBeNull();

    [Theory]
    [InlineData("ghp_abc123")]
    [InlineData("github_pat_abc123")]
    [InlineData("gho_abc123")]
    public void ValidateValue_GithubPat_KnownPrefixes_ReturnNull(string value) =>
        SettingsKeyRegistry.ValidateValue("Github:Pat", value).ShouldBeNull();

    [Fact]
    public void ValidateValue_GithubPat_RandomString_ReturnsError() =>
        SettingsKeyRegistry.ValidateValue("Github:Pat", "my-password").ShouldNotBeNull();

    [Theory]
    [InlineData("Github:RepoOwner", "hoang-snowy")]
    [InlineData("Github:RepoName", "AgentOs")]
    [InlineData("Github:BaseBranch", "feature/x-y")]
    public void ValidateValue_GithubCoordinates_Valid_ReturnNull(string key, string value) =>
        SettingsKeyRegistry.ValidateValue(key, value).ShouldBeNull();

    [Theory]
    [InlineData("Github:RepoOwner", "owner with spaces")]
    [InlineData("Github:RepoName", "repo/slash")]
    [InlineData("Github:BaseBranch", "has space")]
    [InlineData("Github:BaseBranch", "dot..dot")]
    public void ValidateValue_GithubCoordinates_Invalid_ReturnError(string key, string value) =>
        SettingsKeyRegistry.ValidateValue(key, value).ShouldNotBeNull();

    [Fact]
    public void ValidateValue_UnknownKey_ReturnsError() =>
        SettingsKeyRegistry.ValidateValue("Auth:Keycloak:ClientSecret", "x").ShouldNotBeNull();
}
