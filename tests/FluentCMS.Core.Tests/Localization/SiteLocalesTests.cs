using FluentCMS.Content;
using FluentCMS.Core.Localization;
using FluentCMS.Core.Storage.Entities;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Localization;

public sealed class SiteLocalesTests
{
    private static SiteLocale Locale(string code, int position = 0, string? fallbacks = null, bool enabled = true) =>
        new() { Code = code, Position = position, Fallbacks = fallbacks, Enabled = enabled };

    private static SiteLocales Site(params SiteLocale[] locales) => new("en", locales);

    [Fact]
    public void TheImplicitChainStripsSubtagsThenEndsAtTheDefault()
    {
        var site = Site(Locale("en"), Locale("fr", 1), Locale("fr-CA", 2));

        site.Chain("fr-CA").Locales.ShouldBe(["fr-CA", "fr", "en"]);
        site.Chain("fr").Locales.ShouldBe(["fr", "en"]);
        site.Chain("en").Locales.ShouldBe(["en"]);
    }

    [Fact]
    public void TheImplicitChainSkipsAParentTheSiteDoesNotAllow()
    {
        Site(Locale("en"), Locale("fr-CA", 1)).Chain("fr-CA").Locales.ShouldBe(["fr-CA", "en"]);
    }

    [Fact]
    public void AListedChainIsUsedAsItIsAndNeedsNoDefault()
    {
        var site = Site(Locale("en"), Locale("fr", 1), Locale("de", 2), Locale("fr-CA", 3, """["de","fr"]"""));

        site.Chain("fr-CA").Locales.ShouldBe(["fr-CA", "de", "fr"]);
    }

    [Fact]
    public void AnEmptyListMeansNoFallbackAtAll()
    {
        Site(Locale("en"), Locale("fr", 1, "[]")).Chain("fr").Locales.ShouldBe(["fr"]);
    }

    [Fact]
    public void ALocaleTheSiteHasDisabledIsLeftOutOfAChain()
    {
        var site = Site(Locale("en"), Locale("fr", 1, enabled: false), Locale("fr-CA", 2, """["fr","en"]"""));

        site.Chain("fr-CA").Locales.ShouldBe(["fr-CA", "en"]);
    }

    [Fact]
    public void ABrokenListFallsBackToTheImplicitChain()
    {
        Site(Locale("en"), Locale("fr", 1), Locale("fr-CA", 2, "{not json")).Chain("fr-CA").Locales.ShouldBe(["fr-CA", "fr", "en"]);
    }

    [Fact]
    public void ValidateReturnsTheCodeAsTheSiteListsIt()
    {
        var site = Site(Locale("en"), Locale("fr-CA", 1));

        site.Validate("FR-ca").ShouldBe("fr-CA");
        site.Codes.ShouldBe(["en", "fr-CA"]);
    }

    [Theory]
    [InlineData("en_US", "FCMS2311")]
    [InlineData("fr-CA-", "FCMS2311")]
    [InlineData("de", "FCMS2312")]
    public void ValidateRejectsAMalformedOrUnlistedTagWithAProblemCode(string tag, string code)
    {
        var error = Should.Throw<ValidationException>(() => Site(Locale("en")).Validate(tag)).Errors.Single();

        error.Code.ShouldBe(code);
        error.Path.ShouldBe("locale");
    }

    [Fact]
    public void ADisabledLocaleIsNotAccepted()
    {
        Should.Throw<ValidationException>(() => Site(Locale("en"), Locale("fr", 1, enabled: false)).Validate("fr")).Errors.Single().Code.ShouldBe("FCMS2312");
    }
}
