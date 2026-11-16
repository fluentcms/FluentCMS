using Bunit;
using FluentCMS.Admin.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests;

public sealed class ComponentGalleryTests : AdminTestContext
{
    private const string Route = "/system/components";

    [Theory]
    [InlineData(PseudoCulture.LeftToRight, false, "rtl")]
    [InlineData(PseudoCulture.RightToLeft, true, "ltr")]
    public void TheGalleryTakesTheDirectionOfTheInterfaceUntilTheSwitchSaysOtherwise(string culture, bool rightToLeft, string other)
    {
        UseInterfaceCulture(culture);
        Start();
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
        Start();

        var app = RenderGallery();

        app.Find(".cms-root main#main .gallery h1").GetAttribute("tabindex").ShouldBe("-1");
        app.FindAll(".cms-root").ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("Dark", "dark")]
    [InlineData("Light", "light")]
    public void TheThemeChosenInTheGalleryAppliesToTheShell(string mode, string attribute)
    {
        Start();
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
        Services.GetRequiredService<ICmsTextSource>().GetText("Close").ShouldBe(PseudoCulture.Transform("Close", culture == PseudoCulture.RightToLeft));
    }

    [Fact]
    public void TheHomePageLinksToTheGallery()
    {
        Start();

        var app = Render<AdminApp>();

        app.Find("main a[href='system/components']");
    }

    private IRenderedComponent<AdminApp> RenderGallery()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(Route);
        return Render<AdminApp>();
    }
}
