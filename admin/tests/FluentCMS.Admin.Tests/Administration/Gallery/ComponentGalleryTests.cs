using Bunit;
using FluentCMS.Admin.Administration.Gallery;
using FluentCMS.Admin.Components;
using FluentCMS.Admin.Plugins;
using FluentCMS.Admin.Shell;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Administration.Gallery;

// The gallery is a page of the administration assembly that any signed-in user may open, loaded on its first visit (#3354).
public sealed class ComponentGalleryTests : AdministrationTestContext
{
    private const string Route = "system/components";

    [Theory]
    [InlineData(PseudoCulture.LeftToRight, false, "rtl")]
    [InlineData(PseudoCulture.RightToLeft, true, "ltr")]
    public void TheGalleryTakesTheDirectionOfTheInterfaceUntilTheSwitchSaysOtherwise(string culture, bool rightToLeft, string other)
    {
        UseInterfaceCulture(culture);
        var app = RenderGallery();
        // The first switch, in the gallery's header.
        var toggle = app.FindComponents<CmsSwitch>()[0];

        toggle.Instance.Value.ShouldBe(rightToLeft);
        app.Find(".gallery").HasAttribute("dir").ShouldBeFalse();

        app.InvokeAsync(() => toggle.Instance.ValueChanged.InvokeAsync(!rightToLeft));

        app.WaitForAssertion(() => app.Find(".gallery").GetAttribute("dir").ShouldBe(other));
    }

    [Fact]
    public void TheGalleryIsAPageOfTheShell()
    {
        var app = RenderGallery();

        app.Find(".cms-root main#main .gallery h1").GetAttribute("tabindex").ShouldBe("-1");
        app.FindAll(".cms-root").ShouldHaveSingleItem();
    }

    [Fact]
    public void TheGalleryShowsAOneTimeCodeInput()
    {
        var app = RenderGallery();

        app.Find(".gallery input[autocomplete='one-time-code']").GetAttribute("inputmode").ShouldBe("numeric");
    }

    [Theory]
    [InlineData("Dark", "dark")]
    [InlineData("Light", "light")]
    public void TheThemeChosenInTheGalleryAppliesToTheShell(string mode, string attribute)
    {
        var app = RenderGallery();

        app.Find(".gallery-header select").Change(mode);

        app.WaitForAssertion(() => app.Find(".cms-root").GetAttribute("data-cms-theme").ShouldBe(attribute));
    }

    [Theory]
    [InlineData(PseudoCulture.LeftToRight)]
    [InlineData(PseudoCulture.RightToLeft)]
    public void TheWrappedSetsOwnTextsComeThroughTheAdminsLocalizer(string culture)
    {
        UseInterfaceCulture(culture);
        Start();

        var zone = Render<CmsFileDropZone>(p => p.Add(x => x.Label, "Upload"));

        var hint = zone.Find(".cms-field-message").TextContent;
        hint.ShouldBe(PseudoCulture.Transform("Drop files here or press Enter to choose them", culture == PseudoCulture.RightToLeft));
        var texts = Services.GetRequiredService<ICmsTextSource>();
        texts.GetText("Close").ShouldBe(PseudoCulture.Transform("Close", culture == PseudoCulture.RightToLeft));
        // A library key the resources lack: null, so the library shows its own text rather than the key.
        texts.GetText("DataGrid_OptionsMenu").ShouldBeNull();
    }

    [Fact]
    public void TheHomePageLinksToTheGallery()
    {
        var app = Load();

        app.Find("main a[href='system/components']");
        Loader.Loads.ShouldBe(0);
    }

    // Neither the start page nor any other page of the shell fetches the assembly; the gallery's first visit does, once.
    [Fact]
    public void TheGalleryIsFetchedOnItsFirstVisitAndOnce()
    {
        var app = Load();
        Loader.Loads.ShouldBe(0);

        Navigation.NavigateTo(Route);
        app.WaitForAssertion(() => Shown(app).ShouldBe("/" + Route));
        Navigation.NavigateTo("");
        app.WaitForAssertion(() => app.FindAll("main .gallery").ShouldBeEmpty());
        Navigation.NavigateTo(Route);

        app.WaitForAssertion(() => Shown(app).ShouldBe("/" + Route));
        Loader.Loads.ShouldBe(1);
        Services.GetRequiredService<AdminPlugins>().Assemblies.ShouldBe([typeof(GalleryResources).Assembly]);
    }

    // The gallery asks for no permission, and its sidebar has no entry of an area for a user who holds none.
    [Fact]
    public void AUserWithoutAPermissionOfAnAreaOpensTheGallery()
    {
        Grant([]);

        var app = RenderGallery();

        Heading(app).ShouldBe(Localized<GalleryResources>("System.Components.Heading"));
        Links(app).ShouldBe([""]);
    }

    [Fact]
    public void AFailedLoadShowsTheRetryWhichLoadsTheGallery()
    {
        UseNeutralResources();
        Loader.Failures = 1;
        var app = Load(Route);
        app.WaitForAssertion(() => Heading(app).ShouldBe("This page could not load"));

        app.FindAll("main fluent-button").Single(button => button.TextContent.Trim() == "Try again").Click();

        app.WaitForAssertion(() => Shown(app).ShouldBe("/" + Route));
        Loader.Loads.ShouldBe(2);
    }

    [Theory]
    [InlineData("system/components", true)]
    [InlineData("/System/Components/", true)]
    [InlineData("system/components?theme=dark#top", true)]
    [InlineData("system/components/more", false)]
    [InlineData("system", false)]
    [InlineData("system/plugins", false)]
    public void TheGalleryIsTheOpenRoute(string route, bool open) =>
        AdministrationAreas.IsOpen(route).ShouldBe(open);

    private IRenderedComponent<AdminApp> RenderGallery()
    {
        var app = Load(Route);
        app.WaitForAssertion(() => Shown(app).ShouldBe("/" + Route));
        return app;
    }
}
