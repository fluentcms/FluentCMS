using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using FluentCMS.Admin.Components;
using FluentCMS.Admin.Forms;
using FluentCMS.Admin.Interop;
using FluentCMS.Admin.Shell;
using FluentCMS.Admin.Tests.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Shell;

public sealed class ShellTests : AdminTestContext
{
    private static readonly Guid Shop = new("0193a1b2-0000-7000-8000-00000000a002");

    public ShellTests() => UseNeutralResources();

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    private AdminContext Context => Services.GetRequiredService<AdminContext>();

    [Fact]
    public void AfterSignInTheShellReadsTheUserItsSitesAndForTheSiteTheAccessTheSchemaAndThePlugins()
    {
        Load();

        Management.Requests.ShouldBe(
            [("me", null), ("me/sites", null), ("me/access", Main), ("schema", Main), ("plugins", Main)],
            ignoreOrder: true);
        Context.Site!.Id.ShouldBe(MainSite);
        Context.User!.Name.ShouldBe("ada");
    }

    [Fact]
    public void TheSidebarListsTheCollectionsAndThenTheSingletonsInSchemaOrderWithGlobalTypesMarked()
    {
        Management.SetTypes(MainSite, Type("article"), Type("site-settings", "singleton"), Type("tag", scope: "global"), Type("author", displayName: "Writers"));

        var app = Load();

        var sidebar = app.Find(".shell-sidebar nav[aria-label='Main']");
        Sections(sidebar).ShouldBe(["Collections", "Singletons"]);
        Links(sidebar).ShouldBe(["", "content/article", "content/tag", "content/author", "content/site-settings"]);
        sidebar.QuerySelectorAll("a.cms-nav-link").Select(link => link.TextContent.Trim()).ShouldBe(["Home", "Article", "Tag Global", "Writers", "Site settings"]);
    }

    [Fact]
    public void ATypeTheAccessDoesNotListIsLeftOut()
    {
        Management.SetTypes(MainSite, Type("article"), Type("secret"));
        Management.Access[MainSite]["types"]!.AsObject().Remove("secret");

        var app = Load();

        Links(app.Find(".shell-sidebar")).ShouldBe(["", "content/article"]);
    }

    [Fact]
    public void APageOfTheMenuShowsOnlyWithItsPermissionAndWhileItsPluginIsEnabled()
    {
        Services.AddSingleton(new AdminMenu
        {
            Entries =
            [
                new("users", "Shell.Sidebar.Administration", AdminMenuSection.Administration, Permission: "users.manage"),
                new("roles", "Shell.Sidebar.Administration", AdminMenuSection.Administration, Permission: "roles.manage"),
                new("media", "Shell.Sidebar.Media", AdminMenuSection.Media, Plugin: "media"),
                new("webhooks", "Shell.Sidebar.Settings", AdminMenuSection.Settings, Plugin: "webhooks"),
                new("mcp/clients", "Shell.Sidebar.Settings", AdminMenuSection.Settings, Permission: "mcp.manage", Plugin: "mcp"),
            ],
        });
        Management.Plugins[MainSite] = [Plugin("media", enabled: true), Plugin("webhooks", enabled: false), Plugin("mcp", enabled: true)];

        var app = Load();

        var sidebar = app.Find(".shell-sidebar");
        Links(sidebar).ShouldBe(["", "media", "users"]);
        Sections(sidebar).ShouldBe(["Media", "Administration"]);
    }

    [Fact]
    public void AUserWithNoSiteGetsThePageThatSaysSo()
    {
        Management.Sites.Clear();

        var app = Load();

        app.Find("main#main h1").TextContent.ShouldBe("You have no site");
        app.FindAll(".shell-sidebar").ShouldBeEmpty();
        app.FindAll("fluent-menu-button").ShouldHaveSingleItem();
        Management.Count("me/access").ShouldBe(0);
        Management.Count("schema").ShouldBe(0);
    }

    [Fact]
    public void AUserWithoutSchemaReadGetsASidebarWithoutTypesAndANotice()
    {
        Management.SetTypes(MainSite, Type("article"));
        Management.Grant(MainSite, []);

        var app = Load();

        app.Find("main .shell-notice").TextContent.ShouldBe("Your roles in this site do not let you read its content types, so none are listed.");
        Links(app.Find(".shell-sidebar")).ShouldBe([""]);
        Management.Count("schema").ShouldBe(0);
        app.Find("main h1").TextContent.ShouldBe("FluentCMS admin");
    }

    [Fact]
    public void ASchemaReadRefusedAnywayGivesTheNotice()
    {
        Management.SetTypes(MainSite, Type("article"));
        Management.RefusesSchema = true;

        var app = Load();

        app.Find("main .shell-notice");
        Links(app.Find(".shell-sidebar")).ShouldBe([""]);
    }

    [Fact]
    public void SwitchingSiteReadsItsAccessSchemaAndPluginsAndKeepsTheRouteWhenTheTypeIsThere()
    {
        Management.AddSite(Shop, "Shop", "shop");
        Management.SetTypes(MainSite, Type("article"), Type("author"));
        Management.SetTypes(Shop, Type("article"), Type("product"));
        var app = Load("content/article");
        Management.Requests.Clear();

        app.Find(".site-switcher select").Change(Shop.ToString("D"));

        app.WaitForAssertion(() => Links(app.Find(".shell-sidebar")).ShouldBe(["", "content/article", "content/product"]));
        Management.Requests.Where(request => !request.Path.StartsWith("content/", StringComparison.Ordinal))
            .ShouldBe([("me/access", Shop.ToString("D")), ("schema", Shop.ToString("D")), ("plugins", Shop.ToString("D"))], ignoreOrder: true);
        Navigation.Uri.ShouldEndWith("/content/article");
        Storage[TabStorageKeys.Site].ShouldBe(Shop.ToString("D"));
        app.FindAll("[role=status] > div").Select(message => message.TextContent).ShouldContain("Showing the site Shop");
    }

    [Fact]
    public void SwitchingToASiteWithoutTheTypeOfTheRouteGoesToTheStartPage()
    {
        Management.AddSite(Shop, "Shop", "shop");
        Management.SetTypes(MainSite, Type("article"), Type("author"));
        Management.SetTypes(Shop, Type("article"));
        var app = Load("content/author/0193a1b2-0000-7000-8000-000000000009");

        app.Find(".site-switcher select").Change(Shop.ToString("D"));

        app.WaitForAssertion(() => Navigation.Uri.ShouldBe(Navigation.BaseUri));
    }

    [Fact]
    public async Task AReloadStaysInTheSiteChosenLast()
    {
        Management.AddSite(Shop, "Shop", "shop");
        await Storage.SetAsync(TabStorageKeys.Site, Shop.ToString("D"));

        Load();

        Context.Site!.Id.ShouldBe(Shop);
        Management.Requests.ShouldContain(("me/access", Shop.ToString("D")));
    }

    [Fact]
    public async Task TheLastSwitchWinsWhenTheReadsOfAnEarlierOneAnswerLater()
    {
        var third = new Guid("0193a1b2-0000-7000-8000-00000000a003");
        Management.AddSite(Shop, "Shop", "shop");
        Management.AddSite(third, "Third", "third");
        Management.SetTypes(Shop, Type("product"));
        Management.SetTypes(third, Type("event"));
        Management.Grant(Shop, ["schema.read", "shop.manage"]);
        var app = Load();
        Management.HoldSite[Shop] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = app.InvokeAsync(() => Context.SwitchSiteAsync(Shop));
        await app.InvokeAsync(() => Context.SwitchSiteAsync(third));
        Management.HoldSite[Shop].SetResult();
        await first;

        Context.Site!.Id.ShouldBe(third);
        Context.Status.ShouldBe(ShellStatus.Ready);
        Context.Allows("shop.manage").ShouldBeFalse();
        app.WaitForAssertion(() => Links(app.Find(".shell-sidebar")).ShouldBe(["", "content/event"]));
    }

    [Fact]
    public async Task ASwitchForgetsTheAccessOfTheSiteBeforeAndSaysSoOnceTheNewSiteIsSet()
    {
        Management.AddSite(Shop, "Shop", "shop");
        var app = Load();
        Context.Allows("users.manage").ShouldBeTrue();
        List<(ShellStatus Status, Guid? Site, bool Allows)> seen = [];
        Context.Changed += () => seen.Add((Context.Status, Context.Site?.Id, Context.Allows("users.manage")));
        Management.Unavailable = true;

        await app.InvokeAsync(() => Context.SwitchSiteAsync(Shop));

        seen.ShouldBe([(ShellStatus.Loading, Shop, false), (ShellStatus.Failed, Shop, false)]);
        Context.Access.Allows("users.manage").ShouldBeFalse();
        app.WaitForAssertion(() => app.Find("main h1").TextContent.ShouldBe("The admin could not load"));
        app.FindAll(".shell-sidebar").ShouldBeEmpty();

        Management.Unavailable = false;
        app.Find("main fluent-button").Click();

        app.WaitForAssertion(() => Context.Status.ShouldBe(ShellStatus.Ready));
        Context.Site!.Id.ShouldBe(Shop);
        Context.Allows("users.manage").ShouldBeTrue();
    }

    [Fact]
    public void AUserWithOneSiteHasNoSwitcher()
    {
        var app = Load();

        app.FindAll(".site-switcher").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("content/article", new[] { "Content", "Article" })]
    [InlineData("content/article/new", new[] { "Content", "Article", "New entry" })]
    [InlineData("content/article/0193a1b2-0000-7000-8000-000000000009", new[] { "Content", "Article", "0193a1b2-0000-7000-8000-000000000009" })]
    [InlineData("content/site-settings", new[] { "Content", "Site settings" })]
    [InlineData("system/components", new[] { "System", "Components" })]
    [InlineData("", new string[0])]
    public void TheBreadcrumbsFollowTheRoute(string route, string[] expected)
    {
        Management.SetTypes(MainSite, Type("article", displayName: "Article"));

        var app = Load(route);

        app.FindAll("main > nav.cms-breadcrumb li").Select(item => item.TextContent.Trim()).ShouldBe(expected);
    }

    [Fact]
    public void WhileTheShellReadsItShowsThatItIsLoading()
    {
        Management.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Start();

        var app = Render<AdminApp>();

        app.Find("main [role=status]").TextContent.ShouldBe("Loading the admin…");
        app.FindAll(".shell-sidebar").ShouldBeEmpty();
        app.FindAll("main h1").ShouldBeEmpty();
        Management.Hold.SetResult();
        app.WaitForAssertion(() => app.Find("main h1").TextContent.ShouldBe("FluentCMS admin"));
    }

    [Fact]
    public void AStartThatFailsOffersToTryAgain()
    {
        Management.Unavailable = true;
        var app = Load();
        app.Find("main h1").TextContent.ShouldBe("The admin could not load");

        Management.Unavailable = false;
        app.Find("main button, main fluent-button").Click();

        app.WaitForAssertion(() => app.Find("main h1").TextContent.ShouldBe("FluentCMS admin"));
    }

    [Fact]
    public void WithNobodySignedInTheLayoutShowsNoNavigationAndCallsNoApi()
    {
        UseSignInFlow();
        Start();
        Navigation.NavigateTo("authentication/login-callback?code=x&state=y");

        var app = Render<AdminApp>();

        app.WaitForAssertion(() => app.Find("main#main h1").TextContent.ShouldBe("The sign-in did not complete"));
        app.FindAll(".shell-sidebar, .shell-header-end, .shell-open-navigation, nav").ShouldBeEmpty();
        app.Find("header a.shell-home");
        Management.Requests.ShouldBeEmpty();
    }

    [Fact]
    public void TheButtonOfANarrowWindowOpensTheNavigationInADrawerThatANavigationCloses()
    {
        Management.SetTypes(MainSite, Type("article"));
        var app = Load();
        var drawer = app.FindComponent<CmsDrawer>();
        drawer.Instance.Open.ShouldBeFalse();
        drawer.Find("nav[aria-label='Main']");

        app.Find(".shell-open-navigation fluent-button").Click();

        drawer.Instance.Open.ShouldBeTrue();
        Navigation.NavigateTo("content/article");
        app.WaitForAssertion(() => app.FindComponent<CmsDrawer>().Instance.Open.ShouldBeFalse());
    }

    // The library gives focus back to the drawer's opener once it has closed, after the page moved it to its heading, so the
    // layout moves it to the heading again; a drawer the user dismissed leaves focus on the opener (#3178).
    [Fact]
    public async Task ANavigationFromTheDrawerEndsWithFocusOnThePageHeading()
    {
        Management.SetTypes(MainSite, Type("article"));
        var app = Load();
        app.Find(".shell-open-navigation fluent-button").Click();
        var host = DrawerHost(app);

        await host.TriggerEventAsync("ondialogtoggle", new DialogToggleEventArgs { Id = host.Id, NewState = "closed" });

        HeadingFocuses().ShouldBe(0);

        app.Find(".shell-open-navigation fluent-button").Click();
        Navigation.NavigateTo("content/article");
        var focused = HeadingFocuses();
        await DrawerHost(app).TriggerEventAsync("ondialogtoggle", new DialogToggleEventArgs { Id = host.Id, NewState = "closed" });

        HeadingFocuses().ShouldBe(focused + 1);
    }

    [Fact]
    public void TheThemeFollowsTheSystemUntilTheUserChoosesAndTheChoiceIsKeptInTheBrowser()
    {
        var app = Load();
        app.Find(".cms-root").HasAttribute("data-cms-theme").ShouldBeFalse();
        Spoken(MenuItem(app, "System theme")).ShouldBe("System theme, chosen");

        Choose(app, "Dark theme");

        app.WaitForAssertion(() => app.Find(".cms-root").GetAttribute("data-cms-theme").ShouldBe("dark"));
        JSInterop.VerifyInvoke("localStorage.setItem").Arguments.ShouldBe([ThemeStorageInterop.Key, "dark"]);
        Spoken(MenuItem(app, "Dark theme")).ShouldBe("Dark theme, chosen");
        Spoken(MenuItem(app, "System theme")).ShouldBe("System theme");

        Choose(app, "System theme");

        app.WaitForAssertion(() => app.Find(".cms-root").HasAttribute("data-cms-theme").ShouldBeFalse());
        JSInterop.VerifyInvoke("localStorage.removeItem").Arguments.ShouldBe([ThemeStorageInterop.Key]);
    }

    [Fact]
    public void TheThemeChosenBeforeIsTakenAtStart()
    {
        JSInterop.Setup<string?>("localStorage.getItem", ThemeStorageInterop.Key).SetResult("light");

        var app = Load();

        app.WaitForAssertion(() => app.Find(".cms-root").GetAttribute("data-cms-theme").ShouldBe("light"));
    }

    [Theory]
    [InlineData("purple")]
    [InlineData("")]
    [InlineData("Dark")]
    public void AStoredValueOtherThanLightOrDarkFollowsTheSystem(string stored)
    {
        JSInterop.Setup<string?>("localStorage.getItem", ThemeStorageInterop.Key).SetResult(stored);

        var app = Load();

        app.Find(".cms-root").HasAttribute("data-cms-theme").ShouldBeFalse();
        Spoken(MenuItem(app, "System theme")).ShouldBe("System theme, chosen");
    }

    // As in a private window that refuses storage: the choice applies to the page and is not kept.
    [Fact]
    public void AStorageThatThrowsLeavesTheThemeToThePage()
    {
        JSInterop.Setup<string?>("localStorage.getItem", ThemeStorageInterop.Key).SetException(new JSException("The storage is blocked."));
        JSInterop.SetupVoid("localStorage.setItem", _ => true).SetException(new JSException("The storage is blocked."));
        var app = Load();
        app.Find(".cms-root").HasAttribute("data-cms-theme").ShouldBeFalse();

        Choose(app, "Dark theme");

        app.WaitForAssertion(() => app.Find(".cms-root").GetAttribute("data-cms-theme").ShouldBe("dark"));
    }

    [Fact]
    public void TheUserMenuNamesTheUserAndLinksToTheAccountPage()
    {
        var app = Load();

        app.Find("fluent-menu-button").TextContent.Trim().ShouldBe("ada");
        Choose(app, "Account");

        Navigation.Uri.ShouldBe(Navigation.BaseUri + UserMenu.AccountRoute);
    }

    [Fact]
    public void SigningOutWithoutUnsavedFormsSignsOut()
    {
        var app = Load();

        Choose(app, "Sign out");

        ((SignedInTestSession)Services.GetRequiredService<FluentCMS.Admin.Authentication.IAdminSession>()).SignOuts.ShouldBe(1);
    }

    [Fact]
    public void SigningOutWithAnUnsavedFormAsksFirst()
    {
        var app = Load();
        var session = (SignedInTestSession)Services.GetRequiredService<FluentCMS.Admin.Authentication.IAdminSession>();
        using var tracking = Services.GetRequiredService<IFormDraftStore>().Track(new UnsavedForm());

        Choose(app, "Sign out");

        var dialog = app.FindComponents<CmsDialog>().Single(found => found.Instance.Title == "Sign out with unsaved changes?");
        dialog.Instance.Open.ShouldBeTrue();
        session.SignOuts.ShouldBe(0);

        dialog.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Sign out").Click();

        session.SignOuts.ShouldBe(1);
    }

    private static string Main => MainSite.ToString("D");

    private static IElement DrawerHost(IRenderedComponent<AdminApp> app) =>
        app.FindAll("fluent-drawer").Single(drawer => drawer.GetAttribute("aria-label") == "Navigation");

    private int HeadingFocuses() => Document.Invocations.Count(call => call.Identifier == "focusHeading");

    private static IElement MenuItem(IRenderedComponent<AdminApp> app, string text) =>
        app.FindAll("fluent-menu-item").Single(item => item.TextContent.Trim().StartsWith(text, StringComparison.Ordinal));

    // What assistive technology reads of an item: its text without what is hidden from it.
    private static string Spoken(IElement item) =>
        string.Concat(item.GetDescendantsAndSelf().OfType<IText>()
            .Where(text => text.ParentElement?.Closest("[aria-hidden=true]") is null)
            .Select(text => text.Data)).Trim();

    // The library raises the item's click from its menu's change event.
    private static void Choose(IRenderedComponent<AdminApp> app, string text)
    {
        var item = MenuItem(app, text);
        item.TriggerEvent("onmenuitemchange", new MenuItemEventArgs { Id = item.Id });
    }

    private static string[] Links(IElement sidebar) =>
        [.. sidebar.QuerySelectorAll("a.cms-nav-link").Select(link => link.GetAttribute("href")!)];

    private static string[] Sections(IElement sidebar) =>
        [.. sidebar.QuerySelectorAll(".cms-nav-section-title").Select(title => title.TextContent)];

    private IRenderedComponent<AdminApp> Load(string route = "")
    {
        Start();
        Navigation.NavigateTo(route);
        var app = Render<AdminApp>();
        app.WaitForAssertion(() => Context.Status.ShouldNotBe(ShellStatus.Loading));
        return app;
    }

    private sealed class UnsavedForm : IUnsavedForm
    {
        public FormDraft CaptureDraft() => new(new FormDraftKey(null, "article", Guid.Empty, "en"), "\"1\"", new JsonObject());
    }
}
