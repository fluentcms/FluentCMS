using System.Globalization;
using FluentCMS.Admin.Plugins;
using FluentCMS.Admin.Plugins.Sample;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Plugins;

public sealed class AdminPluginCatalogTests
{
    private static readonly Version Running = new(1, 4, 0);

    [Fact]
    public void AManifestThatCannotBeUsedIsListedAndTheOthersAreRead()
    {
        var catalog = AdminPluginCatalog.Read("""
            [
              // A file the build gathered.
              { "name": "media", "assembly": "Media.Admin", "adminRange": "[1.0.0,2.0.0)" },
              { "name": "later", "assembly": "Later.Admin", "adminRange": "[1.0.0,2.0.0)", "menuItems": [{ "type": "P", "label": "L", "route": "/later", "section": "tools" }] },
              { "name": "nulls", "assembly": "Nulls.Admin", "adminRange": "[1.0.0,2.0.0)", "pages": [null] },
              { "name": "broken", "assembly": null },
              { "name": "old", "assembly": "Old.Admin", "adminRange": "[0.1.0,1.0.0)" },
              { "name": "none", "assembly": "None.Admin" },
              { "name": "content", "assembly": "Content.Admin", "adminRange": "1.0.0" },
              { "name": "Media", "assembly": "Other.Admin", "adminRange": "1.0.0" },
              { "name": "webhooks", "assembly": "Webhooks.Admin", "adminRange": "1.0.0" },
            ]
            """, Running);

        catalog.Halves.Select(half => half.Name).ShouldBe(["media", "webhooks"]);
        catalog.Problems.ShouldBe(
        [
            new(PluginProblemKind.Invalid, "later", "Later.Admin"),
            new(PluginProblemKind.Invalid, "nulls", "Nulls.Admin"),
            new(PluginProblemKind.Invalid, "broken", null),
            new(PluginProblemKind.OutOfRange, "old", "Old.Admin", "[0.1.0,1.0.0)"),
            new(PluginProblemKind.OutOfRange, "none", "None.Admin"),
            new(PluginProblemKind.ReservedName, "content", "Content.Admin"),
            new(PluginProblemKind.Duplicate, "Media", "Other.Admin"),
        ]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ \"name\": \"media\" }")]
    [InlineData("[ { ")]
    public void AFileThatIsNoListOfManifestsGivesNoHalfAndOneProblem(string json)
    {
        var catalog = AdminPluginCatalog.Read(json, Running);

        catalog.Halves.ShouldBeEmpty();
        catalog.Problems.ShouldBe([new PluginProblem(PluginProblemKind.Unreadable)]);
    }

    [Fact]
    public void AnEmptyListIsNoHalfAndNoProblem()
    {
        var catalog = AdminPluginCatalog.Read("[\n]", Running);

        catalog.Halves.ShouldBeEmpty();
        catalog.Problems.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("media", "media")]
    [InlineData("Media/presets?tab=1", "media")]
    [InlineData("/media#top", "media")]
    [InlineData("mediax", null)]
    [InlineData("content/media", null)]
    [InlineData("", null)]
    public void TheHalfOfARouteIsTheOneNamedByItsFirstSegment(string route, string? half) =>
        AdminPluginCatalog.Read("""[{ "name": "media", "assembly": "Media.Admin", "adminRange": "1.0.0" }]""", Running).HalfOfRoute(route)?.Name.ShouldBe(half);

    [Theory]
    [InlineData("fr-CA", "Avis")]
    [InlineData("fr", "Avis")]
    [InlineData("de-DE", "Reviews")]
    [InlineData("", "Reviews")]
    public void AMenuItemsLabelIsTheTextOfTheCultureThenOfItsParentsThenOfEnglish(string culture, string label)
    {
        var item = new AdminManifestMenuItem
        {
            Type = typeof(ReviewsPage).FullName!,
            Label = "Reviews.Menu",
            Route = "/reviews",
            Labels = new Dictionary<string, string> { ["en"] = "Reviews", ["fr"] = "Avis", ["de"] = " " },
        };

        item.LabelFor(CultureInfo.GetCultureInfo(culture)).ShouldBe(label);
    }

    [Fact]
    public void AMenuItemWithoutTextsShowsItsKey() =>
        new AdminManifestMenuItem { Type = "P", Label = "Reviews.Menu", Route = "/reviews" }.LabelFor(CultureInfo.GetCultureInfo("fr")).ShouldBe("Reviews.Menu");

    [Fact]
    public void TheHelperNamesAMenuItemWithoutAnEnglishTextACultureThatIsNoneAndAnEmptyText()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, AdminPluginManifest.FileName))
            .Replace("\"labels\": { \"en\": \"Reviews\", \"fr\": \"Avis\" }", "\"labels\": { \"fr\": \"\", \"xx-nowhere\": \"Avis\" }", StringComparison.Ordinal);

        AdminPluginManifestCheck.Differences(typeof(ReviewsAdminPlugin).Assembly, json).ShouldBe(
        [
            "menu item 'Reviews.Menu' of FluentCMS.Admin.Plugins.Sample.ReviewsPage: labels has no text for 'en', which the sidebar shows when it has none of the interface culture",
            "menu item 'Reviews.Menu' of FluentCMS.Admin.Plugins.Sample.ReviewsPage: the label for 'fr' is empty",
            "menu item 'Reviews.Menu' of FluentCMS.Admin.Plugins.Sample.ReviewsPage: labels names 'xx-nowhere', which is not a culture",
        ]);
    }

    [Fact]
    public void ALabelThatIsNullMakesTheManifestInvalid() =>
        AdminPluginManifestJson.Read("""
            { "name": "media", "assembly": "Media.Admin", "menuItems": [{ "type": "P", "label": "L", "route": "/media", "labels": { "en": null } }] }
            """).IsValid.ShouldBeFalse();
}
