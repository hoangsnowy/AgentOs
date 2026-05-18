// AgenticSdlc.Tests/Domain/UserStoryTests.cs
// Phase 3 — Validate logic của UserStory record.

using System;
using AgenticSdlc.Domain.Pipeline;
using Shouldly;
using Xunit;

namespace AgenticSdlc.Tests.Domain;

public class UserStoryTests
{
    [Fact]
    public void Validate_ValidStory_DoesNotThrow()
    {
        var story = new UserStory("Hệ thống quản lý sản phẩm.", NMax: 3, Locale: "vi-VN");
        Should.NotThrow(() => story.Validate());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_BlankDescription_Throws(string? description)
    {
        var story = new UserStory(description!);
        Should.Throw<ArgumentException>(() => story.Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    public void Validate_InvalidNMax_Throws(int nMax)
    {
        var story = new UserStory("Story", NMax: nMax);
        Should.Throw<ArgumentException>(() => story.Validate());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_BlankLocale_Throws(string locale)
    {
        var story = new UserStory("Story", Locale: locale);
        Should.Throw<ArgumentException>(() => story.Validate());
    }

    [Fact]
    public void DefaultValues_LocaleIsVi_NMaxIs3()
    {
        var story = new UserStory("Story");
        story.NMax.ShouldBe(3);
        story.Locale.ShouldBe("vi-VN");
    }
}
