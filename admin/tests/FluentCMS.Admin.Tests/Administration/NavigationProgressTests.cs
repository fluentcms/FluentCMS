using Bunit;
using FluentCMS.Admin.Plugins;
using FluentCMS.Admin.Shell;
using FluentCMS.Admin.Tests.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Administration;

// A navigation that waits for a lazy assembly or for the access of a new content locale shows the shell with a loading state
// (#3446): on the first load, before the router has drawn a page, and inside the app, where the page before stays below it
// and the layout is not made again. A read of a new locale's access that hangs ends in the shell's failed state, with its
// retry, before the client's own timeout.
public sealed class NavigationProgressTests : AdministrationTestContext
{
    private const string LoadingPage = "Loading the page…";

    public NavigationProgressTests() => UseNeutralResources();

    [Theory]
    [InlineData("users")]
    [InlineData("reviews")]
    [InlineData("system/components")]
    public void AFirstLoadOfARouteWhoseAssemblyLoadsShowsTheShellAndALoadingStateUntilThePage(string route)
    {
        UseTheSampleHalf();
        Grant([], "users.manage", "reviews.notes.manage");
        var download = Loader.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var app = Load(route);

        app.WaitForAssertion(() => Loading(app).ShouldBe(LoadingPage));
        app.Find(".shell-sidebar a.cms-nav-link[href='users']");
        app.Find(".shell-header .shell-home");
        app.FindAll("main h1").ShouldBeEmpty();
        Loader.Loads.ShouldBe(1);

        download.SetResult();

        app.WaitForAssertion(() => Shows(app, route).ShouldBeTrue());
        Loading(app).ShouldBeNull();
        app.FindComponents<AdminLayout>().ShouldHaveSingleItem();
        app.FindAll(".shell-header").ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("users")]
    [InlineData("reviews")]
    [InlineData("system/components")]
    public void ANavigationInsideTheAppToARouteWhoseAssemblyIsNotLoadedShowsProgressAtOnceAndKeepsTheLayout(string route)
    {
        UseTheSampleHalf();
        Grant([], "users.manage", "reviews.notes.manage");
        var app = Load();
        var home = Heading(app);
        var layout = app.FindComponent<AdminLayout>().Instance;
        var download = Loader.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Navigation.NavigateTo(route);

        app.WaitForAssertion(() => Loading(app).ShouldBe(LoadingPage));
        Heading(app).ShouldBe(home);
        Loader.Loads.ShouldBe(1);

        download.SetResult();

        app.WaitForAssertion(() => Shows(app, route).ShouldBeTrue());
        Loading(app).ShouldBeNull();
        app.FindComponent<AdminLayout>().Instance.ShouldBeSameAs(layout);
    }

    // Once the assembly is loaded, a navigation waits for nothing, and no frame shows the loading state.
    [Fact]
    public void ANavigationToARouteWhoseAssemblyIsLoadedShowsNoLoadingState()
    {
        Grant([], "users.manage", "roles.manage");
        var app = Load("users");
        app.WaitForAssertion(() => Shown(app).ShouldBe("/users"));
        var shown = 0;
        app.OnMarkupUpdated += (_, _) => shown += Loading(app) is null ? 0 : 1;

        Navigation.NavigateTo("roles");

        app.WaitForAssertion(() => Shown(app).ShouldBe("/roles"));
        shown.ShouldBe(0);
        Loader.Loads.ShouldBe(1);
    }

    // The content locale changes with the address that names it, on an area's route and on a half's.
    [Theory]
    [InlineData("roles")]
    [InlineData("reviews")]
    public void AChangeOfTheContentLocaleShowsProgressWhileItsAccessIsRead(string route)
    {
        UseTheSampleHalf();
        Management.AddLocale(MainSite, "fr", 1);
        Grant(["roles.manage", "reviews.notes.manage"]);
        Management.LocaleAccess[(MainSite, "fr")] = Management.Access[MainSite].DeepClone().AsObject();
        var app = Load(route);
        app.WaitForAssertion(() => Shows(app, route).ShouldBeTrue());
        var layout = app.FindComponent<AdminLayout>().Instance;
        var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Management.HoldPath = path => path.EndsWith("/me/access", StringComparison.Ordinal) ? read.Task : null;
        var reads = Management.Count("me/access");

        Navigation.NavigateTo(route + "?locale=fr");

        app.WaitForAssertion(() => Management.Count("me/access").ShouldBeGreaterThan(reads));
        app.WaitForAssertion(() => Loading(app).ShouldBe(LoadingPage));
        Shows(app, route).ShouldBeTrue();

        read.SetResult();

        app.WaitForAssertion(() => Loading(app).ShouldBeNull());
        Shows(app, route).ShouldBeTrue();
        Context.ContentLocale.ShouldBe("fr");
        app.FindComponent<AdminLayout>().Instance.ShouldBeSameAs(layout);
    }

    // The limit runs on a clock the test moves, which the client's timeout of each attempt does not read: the read is still
    // held when the message shows, and Try again reads the locale's access again.
    [Fact]
    public async Task AReadOfANewLocalesAccessThatHangsEndsInAMessageWithARetryBeforeTheClientsTimeout()
    {
        var time = new MovableTime();
        Services.AddSingleton<TimeProvider>(time);
        Management.AddLocale(MainSite, "fr", 1);
        Grant(["roles.manage"]);
        Management.LocaleAccess[(MainSite, "fr")] = Management.Access[MainSite].DeepClone().AsObject();
        var app = Load("roles");
        app.WaitForAssertion(() => Shown(app).ShouldBe("/roles"));
        var hung = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holding = true;
        Management.HoldPath = path => holding && path.EndsWith("/me/access", StringComparison.Ordinal) ? hung.Task : null;
        var reads = Management.Count("me/access");

        Navigation.NavigateTo("roles?locale=fr");
        app.WaitForAssertion(() => Management.Count("me/access").ShouldBeGreaterThan(reads));
        await app.InvokeAsync(() => time.Advance(AdminContext.LocaleReadLimit - TimeSpan.FromSeconds(1)));
        Context.Status.ShouldBe(ShellStatus.Ready);
        await app.InvokeAsync(() => time.Advance(TimeSpan.FromSeconds(1)));

        app.WaitForAssertion(() => Heading(app).ShouldBe("The admin could not load"));
        Context.Status.ShouldBe(ShellStatus.Failed);
        hung.Task.IsCompleted.ShouldBeFalse();
        holding = false;

        app.FindAll("main fluent-button").Single(button => button.TextContent.Trim() == "Try again").Click();

        app.WaitForAssertion(() => Shown(app).ShouldBe("/roles"));
        Context.ContentLocale.ShouldBe("fr");
        Management.Count("me/access").ShouldBeGreaterThan(reads + 1);
        await app.InvokeAsync(hung.SetResult);
    }

    // The same limit holds when the content locale changes without a navigation, as when the site no longer has it enabled
    // and the shell goes back to the site's default. The schema read it gave up on goes on until the client's timeout, and
    // Try again, which reads the site in that same default locale, sends a new one rather than wait for it.
    [Fact]
    public async Task AReadOfTheDefaultLocalesSchemaThatHangsAfterTheLocaleIsDisabledEndsInAMessageAndTryAgainReadsAnew()
    {
        var time = new MovableTime();
        Services.AddSingleton<TimeProvider>(time);
        Management.AddLocale(MainSite, "fr", 1);
        Grant(["roles.manage"]);
        Management.LocaleAccess[(MainSite, "fr")] = Management.Access[MainSite].DeepClone().AsObject();
        var app = Load("roles?locale=fr");
        app.WaitForAssertion(() => Shown(app).ShouldBe("/roles"));
        app.WaitForAssertion(() => Context.ContentLocale.ShouldBe("fr"));
        Management.Sites[0]!["locales"]!.AsArray()[1]!["enabled"] = false;
        var hung = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holding = true;
        Management.HoldPath = path => holding && path.EndsWith("/schema", StringComparison.Ordinal) ? hung.Task : null;
        var schemas = Management.Count("schema");

        var reading = app.InvokeAsync(() => Context.ReadSitesAsync());
        app.WaitForAssertion(() => Management.Count("schema").ShouldBeGreaterThan(schemas));
        await app.InvokeAsync(() => time.Advance(AdminContext.LocaleReadLimit));
        await reading;

        app.WaitForAssertion(() => Heading(app).ShouldBe("The admin could not load"));
        Context.Status.ShouldBe(ShellStatus.Failed);
        hung.Task.IsCompleted.ShouldBeFalse();
        holding = false;
        schemas = Management.Count("schema");

        app.FindAll("main fluent-button").Single(button => button.TextContent.Trim() == "Try again").Click();

        app.WaitForAssertion(() => Shown(app).ShouldBe("/roles"));
        Context.Status.ShouldBe(ShellStatus.Ready);
        Management.Count("schema").ShouldBeGreaterThan(schemas);
        await app.InvokeAsync(hung.SetResult);
    }
    private static bool Shows(IRenderedComponent<AdminApp> app, string route) =>
        route == "reviews" ? app.FindAll("main .cms-card").Count > 0 : Shown(app) == "/" + route;

    private void UseTheSampleHalf()
    {
        var sample = AdminPluginManifestJson.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, AdminPluginManifest.FileName)));
        Services.AddSingleton(new AdminPluginCatalog([sample with { Widgets = [] }], []));
        Management.Plugins[MainSite] = [Plugin("reviews", enabled: true)];
    }
}
