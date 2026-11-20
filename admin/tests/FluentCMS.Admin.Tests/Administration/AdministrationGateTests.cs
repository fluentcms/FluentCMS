using System.Reflection;
using Bunit;
using FluentCMS.Admin.Administration.Users;
using FluentCMS.Admin.Plugins;
using FluentCMS.Admin.Plugins.Sample;
using FluentCMS.Admin.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Administration;

// Core's administration pages (spec 2.6, 4.15): each area's sidebar entry and its route behind one declaration, for the
// instance where the area is instance-only, and their assembly loaded on the first visit of a route the user may open.
// A page is told by its route and not by what it shows, which is its area's to change.
public sealed class AdministrationGateTests : AdministrationTestContext
{
    private static readonly Guid Shop = new("0193a1b2-0000-7000-8000-00000000a002");

    public AdministrationGateTests() => UseNeutralResources();

    // Each area with a permission that opens it, and whether it needs it for the instance.
    public static TheoryData<string, string, bool> Areas => new()
    {
        { "users", "users.manage", true },
        { "roles", "roles.manage", false },
        { "api-keys", "apikeys.manage", false },
        { "members", "members.manage", true },
        { "plugins", "plugins.manage", false },
        { "jobs", "jobs.manage", true },
        { "system/diagnostics", "system.read", true },
        { "settings", "settings.media.manage", false },
        { "sites", "sites.manage", false },
    };

    [Fact]
    public void TheDeclarationHoldsTheAreasOfTheTheory() =>
        AdministrationAreas.All.Select(area => (area.Route, area.InstanceOnly)).ShouldBe(
            [("users", true), ("roles", false), ("api-keys", false), ("members", true), ("plugins", false), ("jobs", true), ("system/diagnostics", true), ("settings", false), ("sites", false)]);

    [Theory]
    [MemberData(nameof(Areas))]
    public void AnAreaShowsItsEntryAndItsPageToACallerWhoHoldsItsPermission(string route, string permission, bool instanceOnly)
    {
        GrantArea(permission, instanceOnly);

        var app = Load(route);

        app.WaitForAssertion(() => Shown(app).ShouldBe("/" + route));
        Links(app).ShouldBe(["", route]);
        Loader.Loads.ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(Areas))]
    public void WithoutItsPermissionAnAreaHasNoEntryAndItsRouteAnswersNoAccessWithoutLoadingTheAssembly(string route, string permission, bool instanceOnly)
    {
        // Every permission but the area's, in the site and for the instance.
        var others = Everything.Where(name => name != permission).ToArray();
        Grant(others, others);

        var app = Load(route);

        app.WaitForAssertion(() => Heading(app).ShouldBe("No access"));
        Links(app).ShouldBe(["", .. AdministrationAreas.All.Select(area => area.Route).Where(other => other != route)]);
        Loader.Loads.ShouldBe(0);
        AdministrationAreas.OfRoute(route)!.InstanceOnly.ShouldBe(instanceOnly);
    }

    [Theory]
    [InlineData("users", "users.manage")]
    [InlineData("members", "members.manage")]
    [InlineData("jobs", "jobs.manage")]
    [InlineData("system/diagnostics", "system.read")]
    public void AGrantInASiteOnlyOpensNoInstanceOnlyArea(string route, string permission)
    {
        Grant([permission]);

        var app = Load(route);

        app.WaitForAssertion(() => Heading(app).ShouldBe("No access"));
        Links(app).ShouldBe([""]);
        Loader.Loads.ShouldBe(0);
        Context.Allows(permission).ShouldBeTrue();
        Context.AllowsForInstance(permission).ShouldBeFalse();
    }

    // The router knows the page once another area loaded the assembly, and the gate still answers.
    [Fact]
    public void OnceTheAssemblyIsLoadedAnInstanceOnlyRouteStillAnswersNoAccessToAGrantInASite()
    {
        Grant(["roles.manage", "users.manage"]);
        var app = Load("roles");
        app.WaitForAssertion(() => Shown(app).ShouldBe("/roles"));

        Navigation.NavigateTo("users");

        app.WaitForAssertion(() => Heading(app).ShouldBe("No access"));
        Navigation.NavigateTo("Users/");
        app.WaitForAssertion(() => Navigation.Uri.ShouldEndWith("/Users/"));
        Heading(app).ShouldBe("No access");
        Links(app).ShouldBe(["", "roles"]);
    }

    [Fact]
    public void ASuperAdminSeesEveryEntryInItsSectionInTheOrderOfTheDeclaration()
    {
        Grant([], Everything);

        var app = Load();

        Links(app).ShouldBe(["", "users", "roles", "api-keys", "members", "plugins", "jobs", "system/diagnostics", "settings", "sites"]);
        app.Find(".shell-sidebar").QuerySelectorAll(".cms-nav-section-title").Select(title => title.TextContent).ShouldBe(["Administration", "Settings"]);
        app.Find(".shell-sidebar").QuerySelectorAll("a.cms-nav-link").Select(link => link.TextContent.Trim())
            .ShouldBe(["Home", "Users", "Roles", "API keys", "Members", "Plugins", "Jobs", "Diagnostics", "Settings", "Sites and locales"]);
        Loader.Loads.ShouldBe(0);
    }

    [Fact]
    public void TheAssemblyIsLoadedOnTheFirstVisitOfOneOfItsRoutesAndOnce()
    {
        Grant([], Everything);
        var app = Load();
        Loader.Loads.ShouldBe(0);

        Navigation.NavigateTo("users");
        app.WaitForAssertion(() => Shown(app).ShouldBe("/users"));
        Navigation.NavigateTo("roles");
        app.WaitForAssertion(() => Shown(app).ShouldBe("/roles"));
        Navigation.NavigateTo("");
        Navigation.NavigateTo("system/diagnostics");
        app.WaitForAssertion(() => Shown(app).ShouldBe("/system/diagnostics"));

        Loader.Loads.ShouldBe(1);
        Services.GetRequiredService<AdminPlugins>().Assemblies.ShouldBe([typeof(UsersPage).Assembly]);
    }

    [Theory]
    [InlineData("settings.manage")]
    [InlineData("settings..manage")]
    [InlineData("media.settings.manage")]
    public void OnlyThePermissionOfASettingsAreaOpensTheSettingsMenu(string permission)
    {
        Grant([permission], permission);

        var app = Load("settings");

        app.WaitForAssertion(() => Heading(app).ShouldBe("No access"));
        Links(app).ShouldBe([""]);
    }

    [Fact]
    public void ThePermissionOfASettingsAreaHeldForTheInstanceOpensTheSettingsMenu()
    {
        Grant([], "settings.mcp.manage");

        var app = Load("settings");

        app.WaitForAssertion(() => Shown(app).ShouldBe("/settings"));
    }

    [Fact]
    public void AfterASwitchToASiteWhereTheCallerLacksThePermissionTheRouteAnswersNoAccess()
    {
        Management.AddSite(Shop, "Shop", "shop");
        Grant(MainSite, ["roles.manage"]);
        Grant(Shop, []);
        var app = Load("roles");
        app.WaitForAssertion(() => Shown(app).ShouldBe("/roles"));

        app.Find(".site-switcher select").Change(Shop.ToString("D"));

        app.WaitForAssertion(() => Heading(app).ShouldBe("No access"));
        Navigation.Uri.ShouldEndWith("/roles");
        Links(app).ShouldBe([""]);
    }

    [Fact]
    public void AfterASwitchToASiteWhereTheCallerHoldsThePermissionTheRouteShowsItsPage()
    {
        Management.AddSite(Shop, "Shop", "shop");
        Grant(MainSite, []);
        Grant(Shop, ["roles.manage"]);
        var app = Load("roles");
        app.WaitForAssertion(() => Heading(app).ShouldBe("No access"));
        Loader.Loads.ShouldBe(0);

        app.Find(".site-switcher select").Change(Shop.ToString("D"));

        app.WaitForAssertion(() => Shown(app).ShouldBe("/roles"));
        Loader.Loads.ShouldBe(1);
    }

    [Fact]
    public void AFailedLoadShowsTheRetryWhichLoadsTheAssembly()
    {
        GrantArea("jobs.manage", instanceOnly: true);
        Loader.Failures = 1;
        var app = Load("jobs");
        app.WaitForAssertion(() => Heading(app).ShouldBe("This page could not load"));

        app.FindAll("main fluent-button").Single(button => button.TextContent.Trim() == "Try again").Click();

        app.WaitForAssertion(() => Shown(app).ShouldBe("/jobs"));
        Loader.Loads.ShouldBe(2);
    }

    // The load goes on when another navigation overtakes the one that started it, and the router must still get the
    // assembly: without it every route of the areas would be not found until a reload.
    [Fact]
    public void ANavigationAwayDuringTheFirstLoadLeavesTheAreasReachable()
    {
        Grant([], Everything);
        var app = Load();
        var hold = Loader.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Navigation.NavigateTo("users");
        app.WaitForAssertion(() => Loader.Loads.ShouldBe(1));
        Navigation.NavigateTo("account");
        app.WaitForAssertion(() => app.FindComponent<RouteView>().Instance.RouteData.PageType.Name.ShouldBe("AccountPage"));
        hold.SetResult();

        // Nothing renders when the load ends, so the wait is on the load itself and not on a render.
        var administration = Services.GetRequiredService<AdministrationAssembly>();
        SpinWait.SpinUntil(() => administration.Loaded is not null, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        Services.GetRequiredService<AdminPlugins>().Assemblies.ShouldBeEmpty();

        Navigation.NavigateTo("users");

        app.WaitForAssertion(() => Shown(app).ShouldBe("/users"));
        Navigation.NavigateTo("roles");
        app.WaitForAssertion(() => Shown(app).ShouldBe("/roles"));
        Loader.Loads.ShouldBe(1);
    }

    // A loader that fails before it first waits answers a faulted task at once, which must not be kept as the load in
    // progress: the retry would get the same failure for ever.
    [Fact]
    public void ALoadThatFailsAtOnceIsTriedAgainByTheRetry()
    {
        GrantArea("jobs.manage", instanceOnly: true);
        (Loader.FailsAtOnce, Loader.Failures) = (true, 1);
        var app = Load("jobs");
        app.WaitForAssertion(() => Heading(app).ShouldBe("This page could not load"));

        app.FindAll("main fluent-button").Single(button => button.TextContent.Trim() == "Try again").Click();

        app.WaitForAssertion(() => Shown(app).ShouldBe("/jobs"));
        Loader.Loads.ShouldBe(2);
    }

    // The server narrows what the caller holds by the content locale, so the gate answers again when the locale changes
    // without a navigation.
    [Fact]
    public async Task AContentLocaleWhoseAccessLacksThePermissionAnswersNoAccessOnTheAreasRoute()
    {
        Management.AddLocale(MainSite, "fr", 1);
        Grant(["roles.manage"]);
        var french = Management.Access[MainSite].DeepClone().AsObject();
        french["permissions"] = new System.Text.Json.Nodes.JsonArray("schema.read");
        Management.LocaleAccess[(MainSite, "fr")] = french;
        var app = Load("roles");
        app.WaitForAssertion(() => Shown(app).ShouldBe("/roles"));

        await app.InvokeAsync(() => Context.UseLocaleAsync("fr"));

        app.WaitForAssertion(() => Heading(app).ShouldBe("No access"));
        Links(app).ShouldBe([""]);

        await app.InvokeAsync(() => Context.UseLocaleAsync("en"));

        app.WaitForAssertion(() => Shown(app).ShouldBe("/roles"));
    }

    [Fact]
    public async Task AContentLocaleWhoseAccessGrantsThePermissionShowsThePageOfARouteAnsweredNoAccess()
    {
        Management.AddLocale(MainSite, "fr", 1);
        Grant([]);
        var french = Management.Access[MainSite].DeepClone().AsObject();
        french["permissions"] = new System.Text.Json.Nodes.JsonArray("schema.read", "roles.manage");
        Management.LocaleAccess[(MainSite, "fr")] = french;
        var app = Load("roles");
        app.WaitForAssertion(() => Heading(app).ShouldBe("No access"));
        Loader.Loads.ShouldBe(0);

        await app.InvokeAsync(() => Context.UseLocaleAsync("fr"));

        app.WaitForAssertion(() => Shown(app).ShouldBe("/roles"), TimeSpan.FromSeconds(10));
        Loader.Loads.ShouldBe(1);
    }

    [Fact]
    public void ARouteBelowAnAreaThatHasNoPageIsNotFound()
    {
        GrantArea("users.manage", instanceOnly: true);

        var app = Load("users/no/such/page");

        app.WaitForAssertion(() => Heading(app).ShouldBe("Page not found"));
        Loader.Loads.ShouldBe(1);
    }

    // A page of the assembly at a route of no area would be shown to everyone, so the shell shows none: here the sample
    // half's assembly stands in for the administration assembly, and its page at /reviews is outside every area.
    [Fact]
    public void APageOfTheAssemblyOutsideEveryAreaIsNotShown()
    {
        GrantArea("users.manage", instanceOnly: true);
        Loader.Administration = typeof(ReviewsAdminPlugin).Assembly;
        var app = Load("users");
        app.WaitForAssertion(() => Loader.Loads.ShouldBe(1));

        Navigation.NavigateTo("reviews");

        app.WaitForAssertion(() => Navigation.Uri.ShouldEndWith("/reviews"));
        app.WaitForAssertion(() => Heading(app).ShouldBe("Page not found"));
        app.FindAll("main .cms-card").ShouldBeEmpty();
    }

    [Fact]
    public void EveryPageOfTheAssemblyIsAtOrBelowTheRouteOfAnAreaAndEveryAreaHasAPageAtItsRoute()
    {
        var routes = typeof(UsersPage).Assembly.GetTypes().SelectMany(type => type.GetCustomAttributes<RouteAttribute>()).Select(route => route.Template).ToList();

        routes.Where(route => AdministrationAreas.OfRoute(route) is null).ShouldBeEmpty();
        AdministrationAreas.All.Select(area => "/" + area.Route).Except(routes).ShouldBeEmpty();
    }

    // The app trims only the assemblies that opt in, so the pages of this one, which nothing references, are kept whole.
    [Fact]
    public void TheAssemblyDoesNotOptInToTrimmingAndTheShellDoesNotReferenceIt()
    {
        var administration = typeof(UsersPage).Assembly;

        administration.GetName().Name.ShouldBe(AdministrationAreas.AssemblyName);
        administration.GetCustomAttributes<AssemblyMetadataAttribute>().ShouldNotContain(metadata => metadata.Key == "IsTrimmable");
        typeof(AdminApp).Assembly.GetReferencedAssemblies().ShouldNotContain(reference => reference.Name == AdministrationAreas.AssemblyName);
    }

    [Theory]
    [InlineData("users", new[] { "Administration", "Users" })]
    [InlineData("settings", new[] { "Settings" })]
    [InlineData("sites", new[] { "Settings", "Sites and locales" })]
    public void AnAreaIsNamedUnderItsSectionInTheBreadcrumbs(string route, string[] trail)
    {
        Grant([], Everything);

        var app = Load(route);

        app.WaitForAssertion(() => app.Find("main .cms-breadcrumb").QuerySelectorAll("li").Select(item => item.TextContent.Trim()).ShouldBe(trail));
    }

    [Fact]
    public void BelowAnAreasRouteTheBreadcrumbsLinkToTheAreaAndNameThePageByItsSegment()
    {
        Grant([], Everything);

        var app = Load("users/0193a1b2/sessions");

        app.WaitForAssertion(() => app.Find("main .cms-breadcrumb").QuerySelectorAll("li").Select(item => item.TextContent.Trim()).ShouldBe(["Administration", "Users", "0193a1b2"]));
        var link = app.Find("main .cms-breadcrumb a");
        link.GetAttribute("href").ShouldBe("users");
        link.TextContent.ShouldBe("Users");
    }

    // A page below an area's route names itself with PageCrumb once it has read what it shows, and the segment shows
    // again once the page is gone.
    [Fact]
    public void APageNamesTheLastItemOfItsBreadcrumbs()
    {
        Grant([], Everything);
        var app = Load("users/0193a1b2?tab=roles");
        app.WaitForAssertion(() => Trail(app).ShouldBe(["Administration", "Users", "0193a1b2"]));

        var crumb = Render<PageCrumb>(parameters => parameters.Add(item => item.Text, "Ada Lovelace"));

        app.WaitForAssertion(() => Trail(app).ShouldBe(["Administration", "Users", "Ada Lovelace"]));
        app.Find("main .cms-breadcrumb [aria-current=page]").TextContent.ShouldBe("Ada Lovelace");

        crumb.Render(parameters => parameters.Add(item => item.Text, "Ada Byron"));
        app.WaitForAssertion(() => Trail(app).ShouldBe(["Administration", "Users", "Ada Byron"]));

        // The name is of the address it was given at, so another page does not show it.
        Navigation.NavigateTo("users/0193a1b3");
        app.WaitForAssertion(() => Trail(app).ShouldBe(["Administration", "Users", "0193a1b3"]));

        Navigation.NavigateTo("users/0193a1b2");
        app.WaitForAssertion(() => Trail(app).ShouldBe(["Administration", "Users", "Ada Byron"]));
        crumb.Render(parameters => parameters.Add(item => item.Text, null));
        app.WaitForAssertion(() => Trail(app).ShouldBe(["Administration", "Users", "0193a1b2"]));
    }

    // Each area checks the texts of its own pages, with the answers they read, in a file of its own folder, so that no
    // area's issue changes a test that another area's does.
    [Fact]
    public void EveryAreaHasTheLocalizationCheckOfItsPagesInItsOwnFolder()
    {
        var folders = typeof(UsersPage).Assembly.GetTypes()
            .Where(type => type.GetCustomAttributes<RouteAttribute>().Any(route => AdministrationAreas.All.Any(area => "/" + area.Route == route.Template)))
            .Select(type => type.Namespace!.Split('.')[^1])
            .ToList();

        folders.Count.ShouldBe(AdministrationAreas.All.Count);
        folders.Where(folder => typeof(AdministrationGateTests).Assembly.GetType($"{typeof(AdministrationGateTests).Namespace}.{folder}.{folder}LocalizationTests") is not { } check
                || !check.IsSubclassOf(typeof(AdministrationTestContext)))
            .ShouldBeEmpty();
    }

    [Theory]
    [InlineData("users", "users")]
    [InlineData("Users/0193a1b2-0000-7000-8000-00000000a001?tab=roles", "users")]
    [InlineData("/system/diagnostics/", "system/diagnostics")]
    [InlineData("api-keys#new", "api-keys")]
    [InlineData("%75sers", "users")]
    [InlineData("system", null)]
    [InlineData("system/components", null)]
    [InlineData("usersx", null)]
    [InlineData("", null)]
    [InlineData("content/users", null)]
    public void ARouteIsInTheAreaWhoseRouteItIsAtOrBelow(string route, string? area) =>
        AdministrationAreas.OfRoute(route)?.Route.ShouldBe(area);

    // A half's routes begin with its plugin's name, so a plugin named after the first segment of an area could take the
    // area's routes: each is a reserved segment (spec 3.2), and the shell loads no half of such a name.
    [Fact]
    public void NoHalfCanBeNamedAfterTheFirstSegmentOfAnArea()
    {
        foreach (var segment in AdministrationAreas.All.Select(area => area.Route.Split('/')[0]).Distinct())
        {
            var manifest = $$"""[{ "name": "{{segment}}", "assembly": "Some.Admin", "adminRange": "[1.0.0,2.0.0)" }]""";

            var catalog = AdminPluginCatalog.Read(manifest, new Version(1, 0, 0));

            catalog.Halves.ShouldBeEmpty();
            catalog.Problems.ShouldHaveSingleItem().Kind.ShouldBe(PluginProblemKind.ReservedName, segment);
        }
    }

    // The contract of an admin half (spec 5.1): its page declares the same, as the MCP clients page does.
    [Fact]
    public void AHalfsInstanceOnlyPageHasNoMenuItemAndAnswersNoAccessToACallerWhoHoldsItsPermissionInASiteOnly()
    {
        UseTheSampleHalfWithItsPageInstanceOnly();
        Grant(["reviews.notes.manage"]);

        var app = Load("reviews");

        app.WaitForAssertion(() => Heading(app).ShouldBe("No access"));
        Links(app).ShouldBe([""]);
        Loader.Loads.ShouldBe(0);
    }

    // An instance-only page that names no permission has nothing for the caller to hold for the instance, so it is open to
    // no one, and not to everyone as a page without a permission is.
    [Fact]
    public void AHalfsInstanceOnlyPageWithoutAPermissionIsOpenToNoOne()
    {
        UseTheSampleHalfWithItsPageInstanceOnly(permission: null);
        Grant(["reviews.notes.manage"], "reviews.notes.manage");

        var app = Load("reviews");

        app.WaitForAssertion(() => Heading(app).ShouldBe("No access"));
        Links(app).ShouldBe([""]);
        Loader.Loads.ShouldBe(0);
        Context.Allows(null, instanceOnly: true).ShouldBeFalse();
        Context.Allows(null, instanceOnly: false).ShouldBeTrue();
    }

    [Fact]
    public void AHalfsInstanceOnlyPageShowsToACallerWhoHoldsItsPermissionForTheInstance()
    {
        UseTheSampleHalfWithItsPageInstanceOnly();
        Grant([], "reviews.notes.manage");

        var app = Load("reviews");

        app.WaitForAssertion(() => app.Find("main .cms-card p").TextContent.ShouldBe("0"));
        Links(app).ShouldBe(["", "reviews"]);
        Loader.Loads.ShouldBe(1);
    }

    [Fact]
    public void TheManifestCheckNamesAPageThatIsInstanceOnlyOnOneSideOnly()
    {
        var declared = AdminPluginManifestCheck.FromAssembly(typeof(ReviewsAdminPlugin).Assembly);
        var file = AdminPluginManifestJson.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, AdminPluginManifest.FileName)));
        var manifest = file with
        {
            Pages = [.. file.Pages.Select(page => page.Route == "/reviews" ? page with { InstanceOnly = true } : page)],
            MenuItems = [.. file.MenuItems.Select(item => item with { InstanceOnly = true })],
        };

        AdminPluginManifestCheck.Compare(manifest, declared).ShouldBe(
        [
            "page '/reviews' of FluentCMS.Admin.Plugins.Sample.ReviewsPage: instanceOnly is not set in the assembly and 'true' in the manifest",
            "menu item 'Reviews.Menu' of FluentCMS.Admin.Plugins.Sample.ReviewsPage: instanceOnly is not set in the assembly and 'true' in the manifest",
        ]);
        AdminPluginManifestJson.Read(AdminPluginManifestJson.Write(manifest)).Pages.Single(page => page.Route == "/reviews").InstanceOnly.ShouldBeTrue();
    }

    private static string[] Trail(IRenderedComponent<AdminApp> app) =>
        [.. app.Find("main .cms-breadcrumb").QuerySelectorAll("li").Select(item => item.TextContent.Trim())];

    private void UseTheSampleHalfWithItsPageInstanceOnly(string? permission = "reviews.notes.manage")
    {
        var sample = AdminPluginManifestJson.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, AdminPluginManifest.FileName)));
        Services.AddSingleton(new AdminPluginCatalog(
        [
            sample with
            {
                Pages = [.. sample.Pages.Select(page => page with { InstanceOnly = true, Permission = permission })],
                MenuItems = [.. sample.MenuItems.Select(item => item with { InstanceOnly = true, Permission = permission })],
                Widgets = [],
            },
        ],
        []));
        Management.Plugins[MainSite] = [Plugin("reviews", enabled: true)];
    }
}
