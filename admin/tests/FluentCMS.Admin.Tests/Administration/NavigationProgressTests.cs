using Bunit;
using FluentCMS.Admin.Plugins;
using FluentCMS.Admin.Shell;
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
    private const string Loading = "Loading the page…";

    public NavigationProgressTests() => UseNeutralResources();

    [Theory]
    [InlineData("users")]
    [InlineData("reviews")]
    public void AFirstLoadOfARouteWhoseAssemblyLoadsShowsTheShellAndALoadingStateUntilThePage(string route)
    {
        UseTheSampleHalf();
        Grant([], "users.manage", "reviews.notes.manage");
        var download = Loader.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var app = Load(route);

        app.WaitForAssertion(() => Status(app).ShouldBe(Loading));
        app.Find(".shell-sidebar a.cms-nav-link[href='users']");
        app.Find(".shell-header .shell-home");
        app.FindAll("main h1").ShouldBeEmpty();
        Loader.Loads.ShouldBe(1);

        download.SetResult();

        app.WaitForAssertion(() => Shows(app, route).ShouldBeTrue());
        app.FindAll("main [role=status]").ShouldBeEmpty();
        app.FindComponents<AdminLayout>().ShouldHaveSingleItem();
        app.FindAll(".shell-header").ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("users")]
    [InlineData("reviews")]
    public void ANavigationInsideTheAppToARouteWhoseAssemblyIsNotLoadedShowsProgressAtOnceAndKeepsTheLayout(string route)
    {
        UseTheSampleHalf();
        Grant([], "users.manage", "reviews.notes.manage");
        var app = Load();
        var home = Heading(app);
        var layout = app.FindComponent<AdminLayout>().Instance;
        var download = Loader.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Navigation.NavigateTo(route);

        app.WaitForAssertion(() => Status(app).ShouldBe(Loading));
        Heading(app).ShouldBe(home);
        Loader.Loads.ShouldBe(1);

        download.SetResult();

        app.WaitForAssertion(() => Shows(app, route).ShouldBeTrue());
        app.FindAll("main [role=status]").ShouldBeEmpty();
        app.FindComponent<AdminLayout>().Instance.ShouldBeSameAs(layout);
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
        app.WaitForAssertion(() => Status(app).ShouldBe(Loading));
        Shows(app, route).ShouldBeTrue();

        read.SetResult();

        app.WaitForAssertion(() => app.FindAll("main [role=status]").ShouldBeEmpty());
        Shows(app, route).ShouldBeTrue();
        Context.ContentLocale.ShouldBe("fr");
        app.FindComponent<AdminLayout>().Instance.ShouldBeSameAs(layout);
    }

    // The limit runs on a clock a thousand times faster, which the client's timeout of each attempt does not read: the read
    // is still held when the message shows, and Try again reads the locale's access again.
    [Fact]
    public async Task AReadOfANewLocalesAccessThatHangsEndsInAMessageWithARetryBeforeTheClientsTimeout()
    {
        Services.AddSingleton<TimeProvider>(new FastTime());
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

    private static string Status(IRenderedComponent<AdminApp> app) => app.Find("main [role=status]").TextContent;

    private static bool Shows(IRenderedComponent<AdminApp> app, string route) =>
        route == "reviews" ? app.FindAll("main .cms-card").Count > 0 : Shown(app) == "/" + route;

    private void UseTheSampleHalf()
    {
        var sample = AdminPluginManifestJson.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, AdminPluginManifest.FileName)));
        Services.AddSingleton(new AdminPluginCatalog([sample with { Widgets = [] }], []));
        Management.Plugins[MainSite] = [Plugin("reviews", enabled: true)];
    }

    // Every timer of the clock fires a thousand times sooner.
    private sealed class FastTime : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            System.CreateTimer(callback, state, Faster(dueTime), Faster(period));

        private static TimeSpan Faster(TimeSpan span) => span == Timeout.InfiniteTimeSpan || span <= TimeSpan.Zero ? span : span / 1000;
    }
}
