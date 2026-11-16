using System.Globalization;
using Bunit;
using FluentCMS.Admin.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests;

public sealed class LocalizationTests : AdminTestContext
{
    [Theory]
    [InlineData(PseudoCulture.LeftToRight, "/")]
    [InlineData(PseudoCulture.LeftToRight, "/not-found")]
    [InlineData(PseudoCulture.LeftToRight, "/no-such-page")]
    [InlineData(PseudoCulture.RightToLeft, "/")]
    [InlineData(PseudoCulture.RightToLeft, "/not-found")]
    public void EveryTextOfTheAppComesFromTheResources(string culture, string route)
    {
        UseInterfaceCulture(culture);
        Services.GetRequiredService<NavigationManager>().NavigateTo(route);

        var app = Render<AdminApp>();

        PseudoCulture.TextCount(app.Nodes).ShouldBeGreaterThan(1);
        PseudoCulture.Unlocalized(app.Nodes).ShouldBeEmpty();
    }

    [Fact]
    public void TheCheckFailsOnAHardCodedString()
    {
        var page = Render(builder => builder.AddMarkupContent(0, "<p>Saved</p><button aria-label=\"Close\"></button>"));

        PseudoCulture.Unlocalized(page.Nodes).ShouldBe(["Saved", "aria-label=\"Close\""]);
    }

    [Fact]
    public void TheCheckFailsOnAKeyMissingFromTheResources()
    {
        var localizer = Services.GetRequiredService<IStringLocalizer<AdminResources>>();

        var page = Render(builder => builder.AddContent(0, localizer["Shell.Nowhere.Missing"].Value));

        PseudoCulture.Unlocalized(page.Nodes).ShouldBe(["Shell.Nowhere.Missing"]);
    }

    [Fact]
    public void ThePseudoCulturesRunInBothDirections()
    {
        CultureInfo.GetCultureInfo(PseudoCulture.LeftToRight).TextInfo.IsRightToLeft.ShouldBeFalse();
        CultureInfo.GetCultureInfo(PseudoCulture.RightToLeft).TextInfo.IsRightToLeft.ShouldBeTrue();
    }

    [Theory]
    [InlineData(PseudoCulture.LeftToRight, "ltr")]
    [InlineData(PseudoCulture.RightToLeft, "rtl")]
    public void TheDocumentTakesTheLanguageAndDirectionOfTheInterface(string culture, string direction)
    {
        UseInterfaceCulture(culture);

        Render<AdminApp>();

        var invocation = Document.VerifyInvoke("setLanguage");
        invocation.Arguments.ShouldBe([culture, direction]);
    }

    [Theory]
    [InlineData(PseudoCulture.LeftToRight, "ar", "rtl")]
    [InlineData(PseudoCulture.LeftToRight, "en", "ltr")]
    [InlineData(PseudoCulture.RightToLeft, "en", "ltr")]
    [InlineData(PseudoCulture.RightToLeft, "fa-IR", "rtl")]
    public void AFieldTakesTheDirectionOfTheContentLocaleNotOfTheInterface(string culture, string locale, string direction)
    {
        UseInterfaceCulture(culture);

        var field = Render<ContentLocale>(parameters => parameters
            .Add(p => p.Locale, locale)
            .AddChildContent("<input />"));

        var scope = field.Find("div");
        scope.GetAttribute("dir").ShouldBe(direction);
        scope.GetAttribute("lang").ShouldBe(locale);
        scope.QuerySelector("input").ShouldNotBeNull();
    }

    [Fact]
    public void AContentLocaleTheCultureDataDoesNotKnowIsLeftToRight()
    {
        TextDirection.Of("not a locale!").ShouldBe(TextDirection.LeftToRight);
    }
}
