using System.Text.Json.Nodes;
using Bunit;
using Bunit.TestDoubles;
using FluentCMS.Admin.Administration.Users;
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
    private async Task<TaskCompletionSource> HeldVisitAsync(IRenderedComponent<AdminApp> app, string route)
    {
        var hold = Loader.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Navigation.NavigateTo(route);
        app.WaitForAssertion(() => Loader.Loads.ShouldBe(1));
        for (var turn = 0; turn < 3; turn++)
        {
            await app.InvokeAsync(() => { });
        }

        return hold;
    }

    private void AssertLanded(IRenderedComponent<AdminApp> app, string route)
    {
        app.WaitForAssertion(() => app.FindComponent<RouteView>().Instance.RouteData.PageType.ShouldBe(route == "users" ? typeof(UsersPage) : typeof(ReviewsPage)));
        Navigation.Uri.ShouldBe("http://localhost/" + route);
        ((BunitNavigationManager)Navigation).History.Select(entry => entry.Uri).ShouldNotContain($"http://localhost/{route}?locale=fr");
    }

    private static JsonObject Post()
    {
        var title = Field("title");
        title["localizable"] = true;
        var type = Type("post", fields: [title]);
        type["localized"] = true;
        return type;
    }
}
