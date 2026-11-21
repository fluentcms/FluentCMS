using System.Collections.Concurrent;
using Bunit;
using FluentCMS.Admin.Administration.Diagnostics;
using FluentCMS.Admin.Administration.Jobs;
using FluentCMS.Admin.Administration.Members;
using FluentCMS.Admin.Administration.Roles;
using FluentCMS.Admin.Administration.Users;
using FluentCMS.Admin.Plugins;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Administration;

// Navigations that overtake one another on the real Router: it renders the page of the address before again when an
// ancestor renders, and the page of the address when a navigation it cancelled ends, before the OnNavigateAsync of the
// address has run. Each case records the page type every RouteView draws, frame by frame, from before the navigations to
// their end, and no frame may draw a page the caller may not open.
public sealed class AdministrationNavigationTests : AdministrationTestContext
{
    public AdministrationNavigationTests() => UseNeutralResources();

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
    }

    // While the access of the shell's content locale is not read, what the gate would decide with the access of the locale
    // before is held: the page before stays, here for good, as the read never answers, although the caller holds both areas
    // for the instance in the locale before.
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
