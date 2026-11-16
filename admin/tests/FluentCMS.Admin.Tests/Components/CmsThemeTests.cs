using System.Text.RegularExpressions;
using Bunit;
using FluentCMS.Admin.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Components;

public sealed partial class CmsThemeTests : ComponentTestContext
{
    [Fact]
    public void TheSystemModeLeavesTheThemeToPrefersColorScheme()
    {
        var root = Render<CmsThemeRoot>();

        root.Find(".cms-root").HasAttribute("data-cms-theme").ShouldBeFalse();
    }

    [Theory]
    [InlineData(CmsThemeMode.Light, "light")]
    [InlineData(CmsThemeMode.Dark, "dark")]
    public void TheUsersChoiceSetsTheTheme(CmsThemeMode mode, string attribute)
    {
        var root = Render<CmsThemeRoot>(p => p.Add(x => x.Mode, mode));

        root.Find(".cms-root").GetAttribute("data-cms-theme").ShouldBe(attribute);
    }

    [Fact]
    public void TheModeIsPassedToTheLibraryOnlyWhenItChanges()
    {
        var root = Render<CmsThemeRoot>(p => p.Add(x => x.Mode, CmsThemeMode.Dark));
        ThemeCalls().ShouldBe(1);

        root.Render(p => p.Add(x => x.Mode, CmsThemeMode.Dark));
        ThemeCalls().ShouldBe(1);

        root.Render(p => p.Add(x => x.Mode, CmsThemeMode.Light));
        ThemeCalls().ShouldBe(2);
    }

    [Theory]
    [InlineData("#c50f1f")]
    [InlineData("#C5F")]
    public void AHexAccentColorSetsTheAccentToken(string color)
    {
        var root = Render<CmsThemeRoot>(p => p.Add(x => x.AccentColor, color));

        root.Find(".cms-root").GetAttribute("style").ShouldBe($"--cms-color-accent: {color};");
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#123456; background: url(x)")]
    [InlineData("#gggggg")]
    public void AnythingElseKeepsTheDefaultAccent(string color)
    {
        var root = Render<CmsThemeRoot>(p => p.Add(x => x.AccentColor, color));

        root.Find(".cms-root").HasAttribute("style").ShouldBeFalse();
    }

    [Fact]
    public void TheRootLeavesTheDirectionToTheDocumentOrAContainer()
    {
        var root = Render<CmsThemeRoot>();

        root.Find(".cms-root").HasAttribute("dir").ShouldBeFalse();
    }

    [Fact]
    public void TheLogoDefaultsToTheFluentCmsMarkInBothThemes()
    {
        var logo = Render<CmsLogo>();

        var mark = logo.Find(".cms-logo");
        mark.GetAttribute("role").ShouldBe("img");
        mark.GetAttribute("aria-label").ShouldBe("FluentCMS");
        logo.FindAll("img").Select(i => i.GetAttribute("alt")).ShouldAllBe(alt => alt == string.Empty);
        logo.FindAll("img").Count.ShouldBe(2);
    }

    [Fact]
    public void ABrandedLogoReplacesTheMark()
    {
        var logo = Render<CmsLogo>(p => p.Add(x => x.Source, "brand.svg").Add(x => x.AlternativeText, "Contoso"));

        var image = logo.Find("img");
        image.GetAttribute("src").ShouldBe("brand.svg");
        image.GetAttribute("alt").ShouldBe("Contoso");
    }

    [Fact]
    public void ACardIsARegionNamedByItsTitle()
    {
        var card = Render<CmsCard>(p => p.Add(x => x.Title, "Buttons"));

        var heading = card.Find("h2");
        card.Find("section").GetAttribute("aria-labelledby").ShouldBe(heading.Id);
    }

    [Theory]
    [InlineData(CmsStackOrientation.Vertical, CmsSpace.Small, "cms-stack cms-stack-vertical cms-gap-s")]
    [InlineData(CmsStackOrientation.Horizontal, CmsSpace.Large, "cms-stack cms-stack-horizontal cms-gap-l")]
    public void TheStackLaysOutWithTheSpacingTokens(CmsStackOrientation orientation, CmsSpace gap, string classes)
    {
        var stack = Render<CmsStack>(p => p.Add(x => x.Orientation, orientation).Add(x => x.Gap, gap));

        stack.Find("div").GetAttribute("class").ShouldBe(classes);
    }

    [Fact]
    public async Task TheToastServiceRefusesAnEmptyMessage()
    {
        var toasts = Services.GetRequiredService<ICmsToastService>();

        await Should.ThrowAsync<ArgumentException>(() => toasts.ShowAsync(" "));
    }

    [Fact]
    public void TheStylesheetUsesLogicalPropertiesOnly()
    {
        var css = File.ReadAllText(Path.Combine(RepositoryRoot(), "admin", "src", "FluentCMS.Admin.Components", "wwwroot", "cms.css"));

        PhysicalProperty().Matches(css).Select(m => m.Value).ShouldBeEmpty();
    }

    [Fact]
    public void TheStylesheetHasALightAndADarkThemeAndAFocusRing()
    {
        var css = File.ReadAllText(Path.Combine(RepositoryRoot(), "admin", "src", "FluentCMS.Admin.Components", "wwwroot", "cms.css"));

        css.ShouldContain("@media (prefers-color-scheme: dark)");
        css.ShouldContain(".cms-root:not([data-cms-theme=\"light\"])");
        css.ShouldContain(".cms-root[data-cms-theme=\"dark\"]");
        css.ShouldContain("@media (prefers-reduced-motion: reduce)");
        css.ShouldContain("--cms-focus-ring-color");
        css.ShouldContain("--cms-target-size: 24px");
    }

    private int ThemeCalls() => JSInterop.Invocations.Count(i => i.Identifier.Contains("Theme", StringComparison.OrdinalIgnoreCase));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FluentCMS.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }

    // margin-left, padding-right, border-left, left:, right:, float: left, text-align: right and the like.
    [GeneratedRegex(@"(?<![\w-])((margin|padding|border)-(left|right)|(left|right)\s*:|float\s*:\s*(left|right)|text-align\s*:\s*(left|right)|(width|height)\s*:)", RegexOptions.IgnoreCase)]
    private static partial Regex PhysicalProperty();
}
