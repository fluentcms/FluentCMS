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

    [Theory]
    [InlineData("admin/src/FluentCMS.Admin.Components/wwwroot/cms.css")]
    [InlineData("admin/src/FluentCMS.Admin.Administration/Gallery/ComponentGalleryPage.razor.css")]
    [InlineData("admin/src/FluentCMS.Admin/Shell/AdminLayout.razor.css")]
    [InlineData("admin/src/FluentCMS.Admin/Configuration/ConfigurationErrorPage.razor.css")]
    public void TheStylesheetsUseLogicalPropertiesOnly(string path)
    {
        // Physical sides and axes have logical forms; symmetric shorthands (margin: 0, inset: 0, overflow: auto) are exempt.
        PhysicalProperty().Matches(Css(path)).Select(m => m.Value).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("margin-left: 0")]
    [InlineData("padding-right: 1rem")]
    [InlineData("border-left-color: red")]
    [InlineData("left: 0")]
    [InlineData("bottom: 0")]
    [InlineData("width: 1px")]
    [InlineData("min-width: 1px")]
    [InlineData("max-height: 1px")]
    [InlineData("overflow-y: auto")]
    [InlineData("text-align: left")]
    [InlineData("float: right")]
    public void ThePhysicalPropertyCheckCatches(string declaration)
    {
        PhysicalProperty().IsMatch($".x {{ {declaration}; }}").ShouldBeTrue();
    }

    [Theory]
    [InlineData("margin-inline-start: 0")]
    [InlineData("inset-inline-end: 0")]
    [InlineData("min-block-size: 1px")]
    [InlineData("--cms-target-size: 24px")]
    [InlineData("text-align: start")]
    public void ThePhysicalPropertyCheckLetsLogicalPropertiesPass(string declaration)
    {
        PhysicalProperty().IsMatch($".x {{ {declaration}; }}").ShouldBeFalse();
    }

    [Fact]
    public void TheDarkThemeFollowsPrefersColorSchemeUntilTheUserChooses()
    {
        var css = Css(StylesheetPath);

        Rule(css, @"@media \(prefers-color-scheme: dark\)\s*\{\s*\.cms-root:not\(\[data-cms-theme=""light""\]\)").ShouldContain("--cms-color-bg: #12161f");
        Rule(css, @"\.cms-root\[data-cms-theme=""dark""\]").ShouldContain("--cms-color-bg: #12161f");
    }

    [Fact]
    public void TheAccentComesFromTheBrandingOfTheDocument()
    {
        Rule(Css(StylesheetPath), @"\.cms-root").ShouldContain("--cms-color-accent: var(--fluentcms-brand-accent, #154ef2)");
    }

    [Fact]
    public void NativeControlsDrawTheFocusRing()
    {
        var rule = Rule(Css(StylesheetPath), @"\.cms-root :where\([^)]*input[^)]*\):focus-visible");

        rule.ShouldContain("outline: var(--cms-focus-ring-width) solid var(--cms-focus-ring-color)");
    }

    [Fact]
    public void TheLibrarysFocusRingUsesTheFocusToken()
    {
        Rule(Css(StylesheetPath), @"\.cms-root").ShouldContain("--colorStrokeFocus2: var(--cms-focus-ring-color)");
    }

    [Fact]
    public void TargetsAreAtLeast24Pixels()
    {
        var css = Css(StylesheetPath);

        Rule(css, @"\.cms-root").ShouldContain("--cms-target-size: 24px");
        Rule(css, @"\.cms-target").ShouldContain("min-block-size: var(--cms-target-size)");
    }

    [Fact]
    public void ReducedMotionStopsAnimations()
    {
        var css = Css(StylesheetPath);
        var media = Regex.Match(css, @"@media \(prefers-reduced-motion: reduce\)\s*\{(?<body>(?:[^{}]|\{[^{}]*\})*)\}");

        media.Success.ShouldBeTrue();
        media.Groups["body"].Value.ShouldContain("--durationNormal: 0ms");
        media.Groups["body"].Value.ShouldContain("transition-duration: 0.01ms !important");
    }

    [Fact]
    public void TheVisuallyHiddenStatusIsPositionedAgainstTheRoot()
    {
        Rule(Css(StylesheetPath), @"\.cms-root").ShouldContain("position: relative");
    }

    // The app's bundle of scoped styles imports the library's styles; a second import here would load them twice.
    [Fact]
    public void TheStylesheetImportsNothing()
    {
        Css(StylesheetPath).ShouldNotContain("@import");
    }

    [Fact]
    public void TheHostPageHasOneRootAndNamesOnlyTheWrappedSet()
    {
        var page = Css(HostPagePath);

        Regex.Matches(page, @"<link rel=""stylesheet"" href=""([^""]+)""").Select(m => m.Groups[1].Value)
            .ShouldBe(["_content/FluentCMS.Admin.Components/cms.css", "FluentCMS.Admin.App.styles.css"]);
        Regex.Matches(page, @"<div id=""[^""]+""").Select(m => m.Value).ShouldBe(["<div id=\"app\""]);
        page.ShouldNotContain("FluentUI", Case.Insensitive);
        Regex.Count(Css("admin/src/FluentCMS.Admin.App/Program.cs"), @"RootComponents\.Add<").ShouldBe(1);
    }

    [Fact]
    public void TheSkipLinkUsesTheTokensAndTheSharedFocusRing()
    {
        var css = Css(LayoutStylesheetPath);

        var link = Rule(css, @"\.skip-link");
        link.ShouldContain("background-color: var(--cms-color-bg)");
        link.ShouldContain("color: var(--cms-color-accent-text)");
        link.ShouldNotContain("outline");
        Rule(css, @"\.skip-link:focus").ShouldNotContain("outline");
    }

    [Fact]
    public void TheMainContentDropsTheRingOnlyWhenFocusIsNotVisible()
    {
        var css = Css(LayoutStylesheetPath);

        Rule(css, @"main:focus:not\(:focus-visible\)").ShouldContain("outline: none");
        Rule(css, @"main:focus-visible").ShouldContain("outline: var(--cms-focus-ring-width) solid var(--cms-focus-ring-color)");
        Regex.IsMatch(css, @"(?:^|\})\s*main:focus\s*\{").ShouldBeFalse();
    }

    private const string StylesheetPath = "admin/src/FluentCMS.Admin.Components/wwwroot/cms.css";
    private const string LayoutStylesheetPath = "admin/src/FluentCMS.Admin/Shell/AdminLayout.razor.css";
    private const string HostPagePath = "admin/src/FluentCMS.Admin.App/wwwroot/index.html";

    private static string Css(string path) => File.ReadAllText(Path.Combine(RepositoryRoot(), path));

    // The declarations of every rule whose selector matches, joined.
    private static string Rule(string css, string selector)
    {
        var rules = Regex.Matches(css, $@"(?:^|[}};]\s*){selector}\s*\{{(?<body>[^{{}}]*)\}}", RegexOptions.Multiline);
        rules.Count.ShouldBeGreaterThan(0, $"no rule for {selector}");
        return string.Join('\n', rules.Select(r => r.Groups["body"].Value));
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

    // Physical sides (margin-left, border-right-color, top, bottom), sizes (width, min-height), overflow-x and -y, and left or
    // right as a value of float, clear or text-align.
    [GeneratedRegex(@"(?<![\w-])(((margin|padding|border|scroll-margin|scroll-padding)-(left|right|top|bottom)[\w-]*|left|right|top|bottom|(min-|max-)?(width|height)|overflow-[xy])\s*:|(float|clear|text-align)\s*:\s*(left|right))", RegexOptions.IgnoreCase)]
    private static partial Regex PhysicalProperty();
}
