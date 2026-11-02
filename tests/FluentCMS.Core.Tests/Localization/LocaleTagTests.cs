using FluentCMS.Core.Localization;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Localization;

public sealed class LocaleTagTests
{
    [Theory]
    [InlineData("en", "en")]
    [InlineData("EN", "en")]
    [InlineData("fr-ca", "fr-CA")]
    [InlineData("fr-CA", "fr-CA")]
    [InlineData("zh-hant-tw", "zh-Hant-TW")]
    [InlineData("es-419", "es-419")]
    [InlineData("sl-rozaj-biske", "sl-rozaj-biske")]
    [InlineData("de-DE-u-nu-latn", "de-DE-u-nu-latn")]
    [InlineData("yue-HK", "yue-HK")]
    [InlineData("en-x-Test", "en-x-test")]
    [InlineData("x-private", "x-private")]
    public void WellFormedTagsAreNormalizedToTheRecommendedCasing(string tag, string expected)
    {
        LocaleTag.TryNormalize(tag, out var normalized).ShouldBeTrue();
        normalized.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("e")]
    [InlineData("en_US")]
    [InlineData("en-")]
    [InlineData("-en")]
    [InlineData("en--US")]
    [InlineData("en-US-")]
    [InlineData("en-1")]
    [InlineData("12")]
    [InlineData("en-a-b")]
    [InlineData("en-US; drop table")]
    [InlineData("en-Latn-US-variant-too-long-for-the-column")]
    public void MalformedTagsAreRejected(string? tag) => LocaleTag.TryNormalize(tag, out _).ShouldBeFalse();

    [Theory]
    [InlineData("fr-CA", "fr")]
    [InlineData("zh-Hant-TW", "zh-Hant")]
    [InlineData("fr", null)]
    [InlineData("fr-CA-x-a1", "fr-CA")]
    public void TheParentRemovesTheLastSubtagAndAnyExtensionSingleton(string tag, string? parent) => LocaleTag.Parent(tag).ShouldBe(parent);
}
