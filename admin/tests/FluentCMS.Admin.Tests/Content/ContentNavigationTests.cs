using System.Text.Json.Nodes;
using Bunit;
using Bunit.TestDoubles;
using FluentCMS.Admin.Administration.Users;
using FluentCMS.Admin.Components;
using FluentCMS.Admin.Plugins;
using FluentCMS.Admin.Plugins.Sample;
using FluentCMS.Admin.Tests.Administration;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Content;

// A content page on the real Router while the router waits in OnNavigateAsync for a lazy assembly, on the first visit of an
// area or of a half's route: the page drawn before gets the next address's query meanwhile, and reacts to a change of its
// query only while the address is its own route (#3514).
public sealed class ContentNavigationTests : AdministrationTestContext
{
    private readonly FakeContent content;

    public ContentNavigationTests()
    {
        UseNeutralResources();
        content = new FakeContent(Management);
        var sample = AdminPluginManifestJson.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, AdminPluginManifest.FileName)));
        Services.AddSingleton(new AdminPluginCatalog([sample with { Widgets = [] }], []));
        Management.Plugins[MainSite] = [Plugin("reviews", enabled: true)];
        Management.AddLocale(MainSite, "fr", 1);
        Management.SetTypes(MainSite, Post());
        Grant(["reviews.notes.manage"], "users.manage");
        Management.Entries["post"] = [];
    }

    [Theory]
    [InlineData("users")]
    [InlineData("reviews")]
    public async Task AListAboutToBeReplacedFollowsNoLocaleAndReadsNothing(string route)
    {
        var app = LoadContent("content/post?locale=fr&pageSize=50");
        var reads = Management.Count("content/post");

        var hold = await HeldVisitAsync(app, route);

        Management.Count("content/post").ShouldBe(reads);
        Navigation.Uri.ShouldBe("http://localhost/" + route);
        hold.SetResult();
        AssertLanded(app, route);
    }

    [Theory]
    [InlineData("users")]
    [InlineData("reviews")]
    public async Task AnEntryAboutToBeReplacedFollowsNoLocale(string route)
    {
        var id = content.Add("post", new JsonObject { ["title"] = "Hello" });
        content.Add("post", new JsonObject { ["title"] = "Bonjour" }, id: id, locale: "fr");
        var app = LoadContent($"content/post/{id:D}?locale=fr");

        var hold = await HeldVisitAsync(app, route);

        Navigation.Uri.ShouldBe("http://localhost/" + route);
        hold.SetResult();
        AssertLanded(app, route);
    }

    // The check of the address holds after each wait of the page: a change of locale on the list's own route waits for the
    // access of en, the router then waits for the administration assembly at /users, and the access answers meanwhile.
    [Fact]
    public async Task AListWhoseLocaleIsReadWhileTheAddressMovesOnReadsNothing()
    {
        var app = LoadContent("content/post?locale=fr&pageSize=50");
        var access = HoldTheAccessWhileGoingTo(app, "content/post?locale=en&pageSize=50");
        var reads = Management.Count("content/post");

        var hold = await HeldVisitAsync(app, "users", access);

        Management.Count("content/post").ShouldBe(reads);
        Navigation.Uri.ShouldBe("http://localhost/users");
        hold.SetResult();
        AssertLanded(app, "users");
    }

    // The same with an address of the list that names no locale, whose follow waits for the read in flight: it adds no
    // locale to the next address once the read answers.
    [Fact]
    public async Task AFollowThatEndsWhileTheAddressMovesOnAddsNoLocaleToTheNextAddress()
    {
        var app = LoadContent("content/post?locale=fr");
        var access = HoldTheAccessWhileGoingTo(app, "content/post?locale=en");
        Navigation.NavigateTo("content/post?pageSize=50");

        var hold = await HeldVisitAsync(app, "users", access);

        Navigation.Uri.ShouldBe("http://localhost/users");
        hold.SetResult();
        AssertLanded(app, "users");
    }

    // The list is not drawn again with the next address's query: its filters and its search stay as its own address has
    // them, also when the next address has a search of its own.
    [Fact]
    public async Task AListAboutToBeReplacedKeepsItsFiltersAndItsSearch()
    {
        var app = LoadContent("content/post?locale=fr&filter[title][contains]=Hel&q=tour");
        Chips(app).ShouldBe(["Title contains Hel"]);
        SearchBox(app).Instance.Value.ShouldBe("tour");

        var hold = await HeldVisitAsync(app, "users?q=ada");

        Chips(app).ShouldBe(["Title contains Hel"]);
        SearchBox(app).Instance.Value.ShouldBe("tour");
        hold.SetResult();
        AssertLanded(app, "users?q=ada");
    }

    [Fact]
    public void AChangeOfTheQueryOnTheListsOwnRouteReadsAgainAndFollowsTheLocale()
    {
        var app = LoadContent("content/post?locale=fr");
        var reads = Management.Count("content/post");

        Navigation.NavigateTo("content/post?locale=fr&pageSize=50");

        app.WaitForAssertion(() => Management.Count("content/post").ShouldBeGreaterThan(reads));
        Management.ContentQueries[^1].ShouldContain("pageSize=50");

        Navigation.NavigateTo("content/post");

        app.WaitForAssertion(() => Navigation.Uri.ShouldBe("http://localhost/content/post?locale=fr"));
    }

    [Fact]
    public void AChangeOfTheQueryOnTheEntrysOwnRouteFollowsTheLocale()
    {
        var id = content.Add("post", new JsonObject { ["title"] = "Hello" });
        content.Add("post", new JsonObject { ["title"] = "Bonjour" }, id: id, locale: "fr");
        var app = LoadContent($"content/post/{id:D}?locale=fr");

        Navigation.NavigateTo($"content/post/{id:D}");

        app.WaitForAssertion(() => Navigation.Uri.ShouldBe($"http://localhost/content/post/{id:D}?locale=fr"));
    }

    private IRenderedComponent<AdminApp> LoadContent(string route)
    {
        var app = Load(route);
        app.WaitForAssertion(() => Context.ContentLocale.ShouldBe("fr"));
        app.WaitForAssertion(() => app.FindAll(".content-form p[role='status'], table[aria-busy]").ShouldBeEmpty());
        app.WaitForAssertion(() => app.Find("main h1"));
        return app;
    }

    // The first visit of the route in this page load, whose load of the administration assembly or of the half the router
    // waits for: the address is already the route's while the content page stays drawn.
    // With a held read of the access, the visit starts while that read waits, which then answers.
    private async Task<TaskCompletionSource> HeldVisitAsync(IRenderedComponent<AdminApp> app, string route, TaskCompletionSource? access = null)
    {
        var hold = Loader.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Navigation.NavigateTo(route);
        await DrainAsync(app);
        access?.SetResult();

        // The load starts once the access answers, and the content page is not drawn again then, so no render tells of it.
        for (var turn = 0; Loader.Loads == 0 && turn < 500; turn++)
        {
            await Task.Delay(10, Xunit.TestContext.Current.CancellationToken);
        }

        Loader.Loads.ShouldBe(1);
        await DrainAsync(app);
        return hold;
    }

    // Goes to the route with the reads of the access held, once the first has started.
    private TaskCompletionSource HoldTheAccessWhileGoingTo(IRenderedComponent<AdminApp> app, string route)
    {
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = Management.Count("me/access");
        Management.HoldPath = path => path.EndsWith("/me/access", StringComparison.Ordinal) ? held.Task : null;
        Navigation.NavigateTo(route);
        app.WaitForAssertion(() => Management.Count("me/access").ShouldBeGreaterThan(reads));
        return held;
    }

    private void AssertLanded(IRenderedComponent<AdminApp> app, string route)
    {
        var path = route.Split('?')[0];
        app.WaitForAssertion(() => app.FindComponent<RouteView>().Instance.RouteData.PageType.ShouldBe(path == "users" ? typeof(UsersPage) : typeof(ReviewsPage)));
        Navigation.Uri.ShouldBe("http://localhost/" + route);
        ((BunitNavigationManager)Navigation).History.Select(entry => entry.Uri)
            .Where(uri => uri.StartsWith($"http://localhost/{path}", StringComparison.Ordinal)).ShouldAllBe(uri => !uri.Contains("locale="));
    }

    private static async Task DrainAsync(IRenderedComponent<AdminApp> app)
    {
        for (var turn = 0; turn < 3; turn++)
        {
            await app.InvokeAsync(() => { });
        }
    }

    private static List<string> Chips(IRenderedComponent<AdminApp> app) =>
        [.. app.FindAll(".content-filter").Select(chip => chip.QuerySelector("fluent-button")!.TextContent.Trim())];

    private static IRenderedComponent<CmsTextInput> SearchBox(IRenderedComponent<AdminApp> app) =>
        app.FindComponents<CmsTextInput>().Single(input => input.Instance.Label == "Search");

    private static JsonObject Post()
    {
        var title = Field("title");
        title["localizable"] = true;
        title["searchable"] = true;
        var type = Type("post", fields: [title]);
        type["localized"] = true;
        return type;
    }
}
