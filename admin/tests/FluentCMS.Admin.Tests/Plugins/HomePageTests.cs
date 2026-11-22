using System.Reflection;
using System.Text.Json.Nodes;
using Bunit;
using FluentCMS.Admin.Plugins;
using FluentCMS.Admin.Plugins.Sample;
using FluentCMS.Admin.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Plugins;

// The home page with the sample half as the app's admin half, under manifests of the test's own: core's tile of the content
// types, and the widgets the user may see, each loading its half when it shows.
public sealed class HomePageTests : AdminTestContext
{
    private const string Permission = "reviews.notes.manage";
    private const string Sample = "FluentCMS.Admin.Plugins.Sample.";

    private static readonly Guid Shop = new("0193a1b2-0000-7000-8000-00000000a002");

    private readonly CountingLoader loader = new();

    public HomePageTests() => Management.SetTypes(MainSite, Type("article"));

    private static AdminPluginManifest Reviews => AdminPluginManifestJson.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, AdminPluginManifest.FileName)));

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    [Fact]
    public void TheWidgetsShowByPluginNameThenInTheOrderOfTheManifestEachInATileOfItsSize()
    {
        var reviews = Reviews with
        {
            Widgets =
            [
                Widget("LatestReviewsWidget", WidgetSize.Small, Permission, "editor"),
                Widget("ReviewsSettingsPage", WidgetSize.Wide),
                Widget("ReviewPage", WidgetSize.Medium),
            ],
        };
        var alpha = Half("alpha", Widget("ReviewsSettingsPage", WidgetSize.Medium));
        HoldRole("editor", siteId: null);

        var app = Load([reviews, alpha], enabled: ["reviews", "alpha"]);

        app.WaitForAssertion(() => Headings(app).ShouldBe(["Content", "Widget of alpha unavailable", "Latest reviews", "Reviews", "Review"]));
        app.FindAll(".home-tile").Select(tile => tile.ClassList[^1]).ShouldBe(
            ["home-tile-medium", "home-tile-medium", "home-tile-small", "home-tile-wide", "home-tile-medium"]);
        loader.Loads.ShouldBe(["FluentCMS.Admin.Plugins.Sample", "FluentCMS.Admin.Plugins.Sample"], ignoreOrder: true);
    }

    [Fact]
    public void AWidgetOfADisabledPluginIsAbsentAndItsHalfIsNotLoaded()
    {
        var app = Load([Half("reviews", Widget("ReviewsSettingsPage", WidgetSize.Small))], enabled: []);

        app.FindAll(".home-tile").Count.ShouldBe(1);
        app.Find("main .home-types a[href='content/article']").TextContent.ShouldBe("Article");
        loader.Loads.ShouldBeEmpty();
    }

    [Fact]
    public void AWidgetIsAbsentWithoutItsPermissionOrWithItsRoleInAnotherSiteOnly()
    {
        Management.Roles.Add(Role("editor", Shop));
        var widgets = new[] { Widget("ReviewsSettingsPage", WidgetSize.Small, Permission), Widget("ReviewPage", WidgetSize.Small, role: "Editor") };

        var app = Load([Half("reviews", widgets)], enabled: ["reviews"], permitted: false);

        app.FindAll(".home-tile").Count.ShouldBe(1);
        loader.Loads.ShouldBeEmpty();
    }

    [Fact]
    public void ARoleInTheSiteOrInEverySiteShowsTheWidgetsThatAskForIt()
    {
        HoldRole("EDITOR", MainSite);
        var widgets = new[] { Widget("ReviewsSettingsPage", WidgetSize.Small, Permission), Widget("ReviewPage", WidgetSize.Small, role: "editor") };

        var app = Load([Half("reviews", widgets)], enabled: ["reviews"]);

        app.WaitForAssertion(() => Headings(app).ShouldBe(["Content", "Reviews", "Review"]));
    }

    [Fact]
    public void TheHalfLoadsOnlyWhenTheHomePageShowsItsWidget()
    {
        var app = Load([Half("reviews", Widget("ReviewsSettingsPage", WidgetSize.Small))], enabled: ["reviews"], route: "system/plugins");
        loader.Loads.ShouldBeEmpty();

        Navigation.NavigateTo("");

        app.WaitForAssertion(() => Headings(app).ShouldBe(["Content", "Reviews"]));
        loader.Loads.Count.ShouldBe(1);
    }

    [Fact]
    public void AWidgetThatThrowsOrWhoseHalfDoesNotLoadShowsAnErrorInItsTileAndTheOthersRender()
    {
        var reviews = Half("reviews", Widget("ThrowingWidget", WidgetSize.Small), Widget("NoSuchWidget", WidgetSize.Small), Widget("ReviewsSettingsPage", WidgetSize.Small));

        var app = Load([reviews, Half("broken", Widget("ReviewsSettingsPage", WidgetSize.Small))], enabled: ["reviews", "broken"]);

        app.WaitForAssertion(() => Headings(app).ShouldBe(["Content", "Widget of broken unavailable", "Widget of reviews unavailable", "Widget of reviews unavailable", "Reviews"]));
        app.FindAll(".home-tile p").Select(text => text.TextContent).ShouldContain("A widget of the plugin reviews could not be shown.");
        app.FindAll(".home-tile p").Select(text => text.TextContent).ShouldContain("A widget of the plugin broken could not be shown.");
        app.Find("main .home-types a[href='content/article']");
    }

    [Fact]
    public void AWidgetTheRendererCannotCreateShowsAnErrorInItsTileAndTheOthersRender()
    {
        var reviews = Half("reviews", Widget("MisinjectedWidget", WidgetSize.Small), Widget("ThrowingConstructorWidget", WidgetSize.Small), Widget("ReviewsSettingsPage", WidgetSize.Small));

        var app = Load([reviews], enabled: ["reviews"]);

        app.WaitForAssertion(() => Headings(app).ShouldBe(["Content", "Widget of reviews unavailable", "Widget of reviews unavailable", "Reviews"]));
        app.Find("main .home-types a[href='content/article']");
    }

    [Fact]
    public void AWidgetTakesTheAdminsServicesThroughItsConstructorAndOneThatCannotBeCreatedShowsAnErrorInItsTile()
    {
        var reviews = Half("reviews", Widget("ConstructedWidget", WidgetSize.Small), Widget("MisconstructedWidget", WidgetSize.Small), Widget("SetterlessWidget", WidgetSize.Small));

        var app = Load([reviews], enabled: ["reviews"]);

        app.WaitForAssertion(() => Headings(app).ShouldBe(["Content", "Constructed", "Widget of reviews unavailable", "Widget of reviews unavailable"]));
        app.Find("main .home-types a[href='content/article']");
    }

    // The page's own texts, the error tile's among them, come from the resources in either direction; the type's label and the
    // sample widget's texts are data, so they are not checked.
    [Theory]
    [InlineData(PseudoCulture.LeftToRight)]
    [InlineData(PseudoCulture.RightToLeft)]
    public void ThePageAndItsErrorTileAreLocalizedInBothDirections(string culture)
    {
        UseInterfaceCulture(culture);
        var reviews = Half("reviews", Widget("ReviewsSettingsPage", WidgetSize.Small), Widget("ThrowingWidget", WidgetSize.Small));

        var app = Load([reviews], enabled: ["reviews"], neutral: false);

        app.WaitForAssertion(() => app.FindAll("main .home-tile h2").Count.ShouldBe(3));
        var rightToLeft = culture == PseudoCulture.RightToLeft;
        Headings(app)[0].ShouldBe(PseudoCulture.Transform("Content", rightToLeft));
        Headings(app)[2].ShouldBe(PseudoCulture.Transform("Widget of reviews unavailable", rightToLeft));
        app.Find("main h1").TextContent.ShouldBe(PseudoCulture.Transform("FluentCMS admin", rightToLeft));
        var tiles = app.FindAll("main .home-tile");
        PseudoCulture.Unlocalized(app.Find("main h1").ChildNodes).ShouldBeEmpty();
        PseudoCulture.Unlocalized(tiles[0].QuerySelector("h2")!.ChildNodes).ShouldBeEmpty();
        PseudoCulture.Unlocalized(tiles[2].ChildNodes).ShouldBeEmpty();
    }

    [Fact]
    public void CoreTileLinksTheTypesTheUserMayReadInSchemaOrder()
    {
        Management.SetTypes(MainSite, Type("article"), Type("site-settings", "singleton"), Type("author", displayName: "Writers"));
        Management.Access[MainSite]["types"]!.AsObject().Remove("author");

        var app = Load([], enabled: []);

        app.FindAll("main .home-types a").Select(link => (link.GetAttribute("href"), link.TextContent)).ShouldBe(
            [("content/article", "Article"), ("content/site-settings", "Site settings")]);
    }

    [Fact]
    public void WithNoTypeAndNoWidgetThePageSaysSo()
    {
        Management.SetTypes(MainSite);

        var app = Load([Half("reviews", Widget("ReviewsSettingsPage", WidgetSize.Small, Permission))], enabled: ["reviews"], permitted: false);

        app.Find("main p:last-of-type").TextContent.ShouldBe("There is nothing to show you in this site yet.");
        app.FindAll(".home-tiles").ShouldBeEmpty();
    }

    [Fact]
    public void ASiteSwitchShowsTheWidgetsOfTheNewSite()
    {
        Management.AddSite(Shop, "Shop", "shop");
        Management.Grant(Shop, ["schema.read", Permission]);
        Management.Plugins[Shop] = [Plugin("reviews", enabled: false)];
        var app = Load([Half("reviews", Widget("ReviewsSettingsPage", WidgetSize.Small))], enabled: ["reviews"]);
        app.WaitForAssertion(() => Headings(app).ShouldBe(["Content", "Reviews"]));

        app.Find(".site-switcher select").Change(Shop.ToString("D"));

        app.WaitForAssertion(() => app.Find("main p:last-of-type").TextContent.ShouldBe("There is nothing to show you in this site yet."));
    }

    [Fact]
    public void ALocaleWhereTheUserLacksThePermissionHidesTheWidget()
    {
        Management.AddLocale(MainSite, "fr", 1);
        var app = Load([Half("reviews", Widget("ReviewsSettingsPage", WidgetSize.Small, Permission))], enabled: ["reviews"]);
        app.WaitForAssertion(() => Headings(app).ShouldBe(["Content", "Reviews"]));
        Management.LocaleAccess[(MainSite, "fr")] = (JsonObject)Management.Access[MainSite].DeepClone();
        Management.LocaleAccess[(MainSite, "fr")]["permissions"] = new JsonArray("schema.read");

        Navigation.NavigateTo("?locale=fr");

        app.WaitForAssertion(() => Headings(app).ShouldBe(["Content"]));
    }

    private static string[] Headings(IRenderedComponent<AdminApp> app) => [.. app.FindAll("main .home-tile h2").Select(heading => heading.TextContent)];

    private static AdminManifestWidget Widget(string type, WidgetSize size, string? permission = null, string? role = null) =>
        new() { Type = Sample + type, Title = "Reviews.Widget.Title", Size = size, Permission = permission, Role = role };

    // A half of the sample's assembly; one of a name other than reviews fails to load, since the assembly names that plugin.
    private static AdminPluginManifest Half(string name, params AdminManifestWidget[] widgets) =>
        Reviews with { Name = name, Pages = [], MenuItems = [], Widgets = widgets };

    private static JsonObject Role(string name, Guid? site) => new() { ["id"] = Guid.NewGuid(), ["name"] = name, ["siteId"] = site };

    private void HoldRole(string name, Guid? siteId) => Management.Roles.Add(Role(name, siteId));

    private IRenderedComponent<AdminApp> Load(AdminPluginManifest[] halves, string[] enabled, string route = "", bool permitted = true, bool neutral = true)
    {
        if (neutral)
        {
            UseNeutralResources();
        }

        Services.AddSingleton(new AdminPluginCatalog(halves, []));
        Services.AddScoped<IAdminAssemblyLoader>(_ => loader);
        Management.Grant(MainSite, permitted ? ["schema.read", Permission] : ["schema.read"]);
        Management.Plugins[MainSite] = [.. halves.Select(half => (JsonNode)Plugin(half.Name, enabled.Contains(half.Name)))];
        Start();
        Navigation.NavigateTo(route);
        var app = Render<AdminApp>();
        app.WaitForAssertion(() => Services.GetRequiredService<AdminContext>().Status.ShouldBe(ShellStatus.Ready));
        return app;
    }

    // Every half here is of the sample's assembly, already in the process, so a load only counts.
    private sealed class CountingLoader : IAdminAssemblyLoader
    {
        public List<string> Loads { get; } = [];

        public async Task<Assembly> LoadAsync(string assemblyName, CancellationToken cancellationToken)
        {
            Loads.Add(assemblyName);
            await Task.Yield();
            return typeof(ReviewsAdminPlugin).Assembly;
        }
    }
}
