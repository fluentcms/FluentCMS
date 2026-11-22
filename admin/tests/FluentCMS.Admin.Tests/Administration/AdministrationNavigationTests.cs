using System.Collections.Concurrent;
using System.Net;
using Bunit;
using FluentCMS.Admin.Administration.Diagnostics;
using FluentCMS.Admin.Administration.Jobs;
using FluentCMS.Admin.Administration.Members;
using FluentCMS.Admin.Administration.Roles;
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

// Navigations that overtake one another on the real Router: it renders the page of the address before again when an
// ancestor renders, and the page of the address when a navigation it cancelled ends, before the OnNavigateAsync of the
// address has run. Each case records the page type every RouteView draws, frame by frame, from before the navigations to
// their end, and every page the renderer creates, and no frame may draw, nor the renderer create, a page the caller may not
// open.
public sealed class AdministrationNavigationTests : AdministrationTestContext
{
    private static readonly Guid Review = new("0193a1b2-0000-7000-8000-0000000000ff");

    // The pages the renderer creates, by type, in order.
    private readonly ConcurrentQueue<Type> created = new();

    public AdministrationNavigationTests()
    {
        UseNeutralResources();
        ComponentFactories.Add(new CreatedPages(created));
    }

    // An instance-only area the caller holds through a role of the site only, reached by a navigation that overtakes one
    // waiting for the access of its locale.
    [Theory]
    [InlineData("users", "users.manage", typeof(UsersPage))]
    [InlineData("members", "members.manage", typeof(MembersPage))]
    [InlineData("jobs", "jobs.manage", typeof(JobsPage))]
    [InlineData("system/diagnostics", "system.read", typeof(DiagnosticsPage))]
    public async Task ANavigationThatOvertakesOneWaitingForItsLocalesAccessDrawsNoPageTheCallerMayNotOpen(string route, string permission, Type forbidden)
    {
        Management.AddLocale(MainSite, "fr", 1);
        Grant(["roles.manage", permission]);
        Management.LocaleAccess[(MainSite, "fr")] = Management.Access[MainSite].DeepClone().AsObject();
        var app = Load("roles");
        app.WaitForAssertion(() => Shown(app).ShouldBe("/roles"));
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Management.HoldPath = path => path.EndsWith("/me/access", StringComparison.Ordinal) ? held.Task : null;
        var reads = Management.Count("me/access");
        var pages = Record(app);

        Navigation.NavigateTo("roles?locale=fr");
        app.WaitForAssertion(() => Management.Count("me/access").ShouldBeGreaterThan(reads));
        Navigation.NavigateTo(route);
        await DrainAsync(app);
        held.SetResult();

        app.WaitForAssertion(() => Heading(app).ShouldBe("No access"));
        Navigation.Uri.ShouldEndWith("/" + route);
        pages.ShouldNotContain(forbidden);
        created.ShouldNotContain(forbidden);
        pages.ShouldContain(typeof(RolesPage));
    }

    // The same without a locale: the first load of a half's assembly held, overtaken by a navigation to an area the caller
    // may not open.
    [Fact]
    public async Task ANavigationThatOvertakesAHalfsFirstLoadDrawsNoPageTheCallerMayNotOpen()
    {
        var sample = AdminPluginManifestJson.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, AdminPluginManifest.FileName)));
        Services.AddSingleton(new AdminPluginCatalog([sample with { Widgets = [] }], []));
        Management.Plugins[MainSite] = [Plugin("reviews", enabled: true)];
        Grant(["roles.manage", "users.manage", "reviews.notes.manage"]);
        var app = Load("roles");
        app.WaitForAssertion(() => Shown(app).ShouldBe("/roles"));
        var hold = Loader.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pages = Record(app);

        Navigation.NavigateTo("reviews");
        app.WaitForAssertion(() => Loader.Loads.ShouldBe(2));
        Navigation.NavigateTo("users");
        await DrainAsync(app);
        hold.SetResult();

        app.WaitForAssertion(() => Heading(app).ShouldBe("No access"));
        Navigation.Uri.ShouldEndWith("/users");
        pages.ShouldNotContain(typeof(UsersPage));
        created.ShouldNotContain(typeof(UsersPage));
    }

    // While the access of the shell's content locale is not read, what the gate would decide with the access of the locale
    // before is held: the page before stays, under the layout's loading state, as long as the read does not answer, although
    // the caller holds both areas for the instance in the locale before.
    [Fact]
    public async Task WhileTheAccessOfTheContentLocaleIsUnreadThePageBeforeStays()
    {
        Management.AddLocale(MainSite, "fr", 1);
        Grant([], "users.manage", "roles.manage");
        Management.LocaleAccess[(MainSite, "fr")] = Management.Access[MainSite].DeepClone().AsObject();
        var app = Load("roles");
        app.WaitForAssertion(() => Shown(app).ShouldBe("/roles"));
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Management.HoldPath = path => path.EndsWith("/me/access", StringComparison.Ordinal) ? never.Task : null;
        var reads = Management.Count("me/access");
        var pages = Record(app);

        Navigation.NavigateTo("roles?locale=fr");
        app.WaitForAssertion(() => Management.Count("me/access").ShouldBeGreaterThan(reads));
        Navigation.NavigateTo("users");
        await DrainAsync(app);

        Navigation.Uri.ShouldEndWith("/users");
        Context.ContentLocale.ShouldBe("fr");
        Context.AccessCurrent.ShouldBeFalse();
        pages.ShouldNotContain(typeof(UsersPage));
        Shown(app).ShouldBe("/roles");
        Loading(app).ShouldBe("Loading the page…");

        // Let the read end, so that nothing waits once the test is over.
        never.SetResult();
    }

    // Two navigations in quick succession, neither of which waits for anything.
    [Fact]
    public async Task TwoQuickNavigationsDrawNoPageTheCallerMayNotOpen()
    {
        Grant(["roles.manage", "users.manage", "apikeys.manage"]);
        var app = Load("roles");
        app.WaitForAssertion(() => Shown(app).ShouldBe("/roles"));
        var pages = Record(app);

        Navigation.NavigateTo("api-keys");
        Navigation.NavigateTo("users");
        await DrainAsync(app);

        app.WaitForAssertion(() => Heading(app).ShouldBe("No access"));
        pages.ShouldNotContain(typeof(UsersPage));
    }

    // A navigation cancelled while the assembly loads for the first time, then a route the caller may not open, and the
    // areas once the assembly is loaded.
    [Fact]
    public async Task ANavigationCancelledDuringTheFirstLoadThenAForbiddenAreaDrawNoPageTheCallerMayNotOpen()
    {
        Grant(["roles.manage", "users.manage"]);
        var app = Load();
        var hold = Loader.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pages = Record(app);

        Navigation.NavigateTo("roles");
        app.WaitForAssertion(() => Loader.Loads.ShouldBe(1));
        Navigation.NavigateTo("users");
        await DrainAsync(app);
        hold.SetResult();
        app.WaitForAssertion(() => Heading(app).ShouldBe("No access"));

        Navigation.NavigateTo("roles");
        app.WaitForAssertion(() => Shown(app).ShouldBe("/roles"));
        Navigation.NavigateTo("users");
        app.WaitForAssertion(() => Heading(app).ShouldBe("No access"));
        pages.ShouldNotContain(typeof(UsersPage));
        Loader.Loads.ShouldBe(1);
    }

    // The navigation that a cancelled one's end renders before its OnNavigateAsync has run names another content locale,
    // whose access the shell has not read yet: the access of the locale before, which lets the caller open the area, must
    // not open it there. The caller holds users for the instance, or roles through a role of the site, in the default
    // locale only. The access of fr is held, and then answers.
    [Theory]
    [InlineData("users", false, typeof(UsersPage))]
    [InlineData("users", true, typeof(UsersPage))]
    [InlineData("roles", true, typeof(RolesPage))]
    public async Task ANavigationToAnotherLocaleRenderedBeforeItsOwnWaitDrawsNoPageTheLocaleDenies(string route, bool holdTheLocalesAccess, Type forbidden)
    {
        UseTheSampleHalf();
        Management.AddLocale(MainSite, "fr", 1);
        Grant(["roles.manage", "apikeys.manage", "reviews.notes.manage"], "users.manage");
        var french = Management.Access[MainSite].DeepClone().AsObject();
        french["instancePermissions"] = new System.Text.Json.Nodes.JsonArray();
        french["permissions"] = route == "users"
            ? new System.Text.Json.Nodes.JsonArray("schema.read", "roles.manage", "apikeys.manage", "reviews.notes.manage")
            : new System.Text.Json.Nodes.JsonArray("schema.read", "apikeys.manage", "reviews.notes.manage");
        Management.LocaleAccess[(MainSite, "fr")] = french;
        var app = Load(route);
        app.WaitForAssertion(() => Shown(app).ShouldBe("/" + route));
        Navigation.NavigateTo("api-keys");
        app.WaitForAssertion(() => Shown(app).ShouldBe("/api-keys"));
        var download = Loader.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (holdTheLocalesAccess)
        {
            Management.HoldPath = path => path.EndsWith("/me/access", StringComparison.Ordinal) ? read.Task : null;
        }

        created.Clear();
        var pages = Record(app);

        Navigation.NavigateTo("reviews");
        app.WaitForAssertion(() => Loader.Loads.ShouldBe(2));
        Navigation.NavigateTo(route + "?locale=fr");
        await DrainAsync(app);
        read.SetResult();
        download.SetResult();

        app.WaitForAssertion(() => Heading(app).ShouldBe("No access"));
        Context.ContentLocale.ShouldBe("fr");
        created.ShouldNotContain(forbidden);
        pages.ShouldNotContain(forbidden);
    }

    // A half's class with two routes of different permissions: /reviews/{Id:guid} needs reviews.notes.manage, which the
    // caller lacks, and /reviews/new reviews.notes.create, which it holds. The page answered no access at a review's address
    // is drawn again, when the access of fr is read, while the router holds the navigation to /reviews/new?locale=fr: the
    // route data is still the review's, and no frame may draw the page with its id.
    [Fact]
    public async Task AHalfsPageIsGatedByTheRouteItsRouteDataAgreesWithAndNotByTheAddress()
    {
        UseTheSampleHalf();
        Management.AddLocale(MainSite, "fr", 1);
        Grant(["roles.manage", "reviews.notes.create"]);
        Management.LocaleAccess[(MainSite, "fr")] = Management.Access[MainSite].DeepClone().AsObject();
        var app = Load("reviews/new");
        app.WaitForAssertion(() => app.FindComponent<RouteView>().Instance.RouteData.PageType.ShouldBe(typeof(ReviewPage)));
        Navigation.NavigateTo($"reviews/{Review:D}");
        app.WaitForAssertion(() => Heading(app).ShouldBe("No access"));
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Management.HoldPath = path => path.EndsWith("/me/access", StringComparison.Ordinal) ? held.Task : null;
        var reads = Management.Count("me/access");
        var drawn = new ConcurrentQueue<string>();
        app.OnMarkupUpdated += (_, _) =>
        {
            foreach (var view in app.FindComponents<RouteView>().Where(view => view.Instance.RouteData.PageType == typeof(ReviewPage)))
            {
                drawn.Enqueue(view.Instance.RouteData.RouteValues.GetValueOrDefault("Id")?.ToString() ?? "new");
            }
        };

        Navigation.NavigateTo("reviews/new?locale=fr");
        app.WaitForAssertion(() => Management.Count("me/access").ShouldBeGreaterThan(reads));
        held.SetResult();

        app.WaitForAssertion(() => drawn.ShouldContain("new"));
        drawn.ShouldNotContain(Review.ToString());
        app.FindComponent<ReviewPage>().Instance.Id.ShouldBeNull();
    }

    // After a failed read of the access of fr, the layout's Try again reads the site in its default locale first and fr
    // after it: the roles page, which fr denies and which the layout showed before the failure, is not created again.
    [Fact]
    public void AfterAFailedReadOfALocalesAccessTryAgainCreatesNoPageTheLocaleDenies()
    {
        Management.AddLocale(MainSite, "fr", 1);
        Grant(["roles.manage"]);
        var french = Management.Access[MainSite].DeepClone().AsObject();
        french["permissions"] = new System.Text.Json.Nodes.JsonArray("schema.read");
        Management.LocaleAccess[(MainSite, "fr")] = french;
        var app = Load("roles");
        app.WaitForAssertion(() => Shown(app).ShouldBe("/roles"));
        var fail = 1;
        Management.Respond = (request, path, _) =>
            path == "me/access" && request.Headers.TryGetValues("X-Cms-Locale", out var locales) && locales.Single() == "fr" && Interlocked.Exchange(ref fail, 0) == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : null;
        Navigation.NavigateTo("roles?locale=fr");
        app.WaitForAssertion(() => Context.Status.ShouldBe(ShellStatus.Failed));
        created.Clear();
        var pages = Record(app);

        app.FindAll("main fluent-button").Single(button => button.TextContent.Trim() == "Try again").Click();

        app.WaitForAssertion(() => Heading(app).ShouldBe("No access"));
        app.WaitForAssertion(() => Context.AccessFor("fr").ShouldBeTrue());
        Heading(app).ShouldBe("No access");
        created.ShouldNotContain(typeof(RolesPage));
        pages.ShouldNotContain(typeof(RolesPage));
    }

    // The no-access and not-found states are one page, which the RouteView keeps from one route to the next: its heading
    // follows the address after a link, and the layout stays. The caller holds users and jobs through a role of the site
    // only, so their instance-only areas answer no access, before the assembly is loaded and once roles has loaded it.
    [Theory]
    [InlineData("users", "no-such-page")]
    [InlineData("no-such-page", "users")]
    [InlineData("no-such-page", "jobs")]
    [InlineData("roles", "users", "no-such-page", "jobs")]
    [InlineData("roles", "no-such-page", "users", "no-such-page")]
    public void TheNoAccessAndNotFoundStatesFollowTheAddressAndKeepTheLayout(params string[] routes)
    {
        Grant(["roles.manage", "users.manage", "jobs.manage"]);
        var app = Load(routes[0]);
        app.WaitForAssertion(() => Heading(app).ShouldBe(HeadingOf(routes[0])));
        var layout = app.FindComponent<AdminLayout>().Instance;

        foreach (var route in routes[1..])
        {
            Navigation.NavigateTo(route);
            app.WaitForAssertion(() => Heading(app).ShouldBe(HeadingOf(route)));
        }

        app.FindComponent<AdminLayout>().Instance.ShouldBeSameAs(layout);

        static string HeadingOf(string route) => route switch
        {
            "roles" => "Roles",
            "users" or "jobs" => "No access",
            _ => "Page not found",
        };
    }

    // The first download of a half's assembly, overtaken by its route in a content locale whose access lacks the page: the
    // router answers the address before its navigation has read that access, and the heading follows once it has.
    [Fact]
    public async Task AHalfsFirstDownloadOvertakenByItsRouteInALocaleThatDeniesItEndsOnNoAccess()
    {
        UseTheSampleHalf();
        Management.AddLocale(MainSite, "fr", 1);
        Grant(["roles.manage", "reviews.notes.manage"]);
        var french = Management.Access[MainSite].DeepClone().AsObject();
        french["permissions"] = new System.Text.Json.Nodes.JsonArray("schema.read", "roles.manage");
        Management.LocaleAccess[(MainSite, "fr")] = french;
        var app = Load("roles");
        app.WaitForAssertion(() => Shown(app).ShouldBe("/roles"));
        var layout = app.FindComponent<AdminLayout>().Instance;
        var download = Loader.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Navigation.NavigateTo("reviews");
        app.WaitForAssertion(() => Loader.Loads.ShouldBe(2));
        Navigation.NavigateTo("reviews?locale=fr");
        await DrainAsync(app);
        download.SetResult();

        app.WaitForAssertion(() => Heading(app).ShouldBe("No access"));
        Context.ContentLocale.ShouldBe("fr");
        app.FindAll("main .cms-card").ShouldBeEmpty();
        app.FindComponent<AdminLayout>().Instance.ShouldBeSameAs(layout);
    }

    // The page type every RouteView draws, after each update of the markup.
    private static ConcurrentQueue<Type> Record(IRenderedComponent<AdminApp> app)
    {
        var pages = new ConcurrentQueue<Type>();
        app.OnMarkupUpdated += (_, _) =>
        {
            foreach (var view in app.FindComponents<RouteView>())
            {
                pages.Enqueue(view.Instance.RouteData.PageType);
            }
        };
        return pages;
    }

    // The sample half as the app's one half, enabled in the site.
    private void UseTheSampleHalf()
    {
        var sample = AdminPluginManifestJson.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, AdminPluginManifest.FileName)));
        Services.AddSingleton(new AdminPluginCatalog([sample with { Widgets = [] }], []));
        Management.Plugins[MainSite] = [Plugin("reviews", enabled: true)];
    }

    // Records every page the renderer creates, and leaves the creation to the renderer.
    private sealed class CreatedPages(ConcurrentQueue<Type> created) : IComponentFactory
    {
        public bool CanCreate(Type componentType)
        {
            if (componentType.Name.EndsWith("Page", StringComparison.Ordinal))
            {
                created.Enqueue(componentType);
            }

            return false;
        }

        public IComponent Create(Type componentType) => throw new NotSupportedException();
    }

    // The work the navigations queued on the renderer's dispatcher has run, as a cancelled navigation's end and the render
    // that follows it.
    private static async Task DrainAsync(IRenderedComponent<AdminApp> app)
    {
        for (var turn = 0; turn < 3; turn++)
        {
            await app.InvokeAsync(() => { });
        }
    }
}
