using System.Reflection;
using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using FluentCMS.Admin.Administration.Common;
using FluentCMS.Admin.Administration.Users;
using FluentCMS.Admin.Authentication;
using FluentCMS.Admin.Components;
using FluentCMS.Admin.Forms;
using FluentCMS.Admin.Plugins;
using FluentCMS.Admin.Plugins.Sample;
using FluentCMS.Admin.Shell;
using FluentCMS.Admin.Tests.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.FluentUI.AspNetCore.Components;
using Shouldly;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Administration;

// The shell with the administration assembly behind a loader that counts its loads, since the assembly is in the test's
// process already. A test of an area derives from it, in a folder of its own beside this file, and sets what the fake
// Management API answers its page with Management.Answer or Management.Respond before it loads a route.
public abstract class AdministrationTestContext : AdminTestContext
{
    // A permission that opens each area, for the instance, as a Super Admin holds them.
    private protected static readonly string[] Everything =
        [.. AdministrationAreas.All.Select(area => area.Permission.Replace("*", "media", StringComparison.Ordinal))];

    private protected AdministrationLoader Loader { get; } = new();

    private protected NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    private protected AdminContext Context => Services.GetRequiredService<AdminContext>();

    // What the user holds in a site, and what it holds for the instance, which the server lists in the site too.
    private protected void Grant(Guid site, string[] inSite, params string[] forInstance)
    {
        Management.Grant(site, ["schema.read", .. inSite, .. forInstance]);
        Management.Access[site]["instancePermissions"] = new JsonArray([.. forInstance.Select(name => (JsonNode)name)]);
    }

    private protected void Grant(string[] inSite, params string[] forInstance) => Grant(MainSite, inSite, forInstance);

    // The grant an area's page needs: for the instance when the area is instance-only, in the site otherwise.
    private protected void GrantArea(string permission, bool instanceOnly) => Grant(instanceOnly ? [] : [permission], instanceOnly ? [permission] : []);

    private protected IRenderedComponent<AdminApp> Load(string route = "")
    {
        Services.AddScoped<IAdminAssemblyLoader>(_ => Loader);
        Start();
        Navigation.NavigateTo(route);
        var app = Render<AdminApp>();
        app.WaitForAssertion(() => Context.Status.ShouldBe(ShellStatus.Ready));
        return app;
    }

    // The check of spec 3.1 on a route of an area, which the area's own test runs with the answers its page reads: as a
    // caller who holds every area's permission, under a pseudo-culture, once the page shows what `ready` asserts, the page
    // has one heading and no text that is not from the resources. Data the fake serves is marked as a resource is, with
    // PseudoCulture.Transform.
    private protected IRenderedComponent<AdminApp> LoadLocalized(string culture, string route, Action<IRenderedComponent<AdminApp>>? ready = null)
    {
        UseInterfaceCulture(culture);
        Grant([], Everything);

        var app = Load(route);

        app.WaitForAssertion(() => Shown(app).ShouldNotBeNull());
        if (ready is not null)
        {
            app.WaitForAssertion(() => ready(app));
        }

        app.FindAll("main h1").ShouldHaveSingleItem();
        PseudoCulture.Unlocalized(app.Nodes).ShouldBeEmpty();
        return app;
    }

    // A text of an area's resources as the interface shows it.
    private protected string Localized<TResources>(string key, params object[] arguments) =>
        Services.GetRequiredService<IStringLocalizer<TResources>>()[key, arguments];

    private protected static string Heading(IRenderedComponent<AdminApp> app) => app.Find("main h1").TextContent;

    // The layout's loading state while a navigation waits for what its page needs (#3446), or null while it shows none.
    private protected static string? Loading(IRenderedComponent<AdminApp> app) =>
        app.FindAll("main .shell-notice[role=status]").SingleOrDefault()?.TextContent;

    private protected static string[] Links(IRenderedComponent<AdminApp> app) =>
        [.. app.Find(".shell-sidebar").QuerySelectorAll("a.cms-nav-link").Select(link => link.GetAttribute("href")!)];

    // The first route of the page of the administration assembly that the router shows, or none while it shows another.
    private protected static string? Shown(IRenderedComponent<AdminApp> app)
    {
        var page = app.FindComponent<RouteView>().Instance.RouteData.PageType;
        return page.Assembly == typeof(UsersPage).Assembly ? page.GetCustomAttributes<RouteAttribute>().First().Template : null;
    }

    // The dirty guard of an editor of the areas (#3511), on a page a test has changed, with a second site to switch to: the tab's close asks, and a route to another page, a site switch and a sign-out ask first and
    // leave the page as it is on the answer to stay, which `kept` checks. The end of the session keeps nothing of the page
    // in the draft store, none of the texts `typed` in sessionStorage, and the question with it.
    private protected async Task LeavingAsksFirstAsync(IRenderedComponent<AdminApp> app, Action kept, params string[] typed)
    {
        var page = Navigation.Uri;
        UnloadGuards().ShouldNotBeEmpty();
        UnloadGuards()[^1].ShouldBeTrue();

        Navigation.NavigateTo("");
        app.WaitForAssertion(() => Dialog(app, LeaveTitle).Instance.Open.ShouldBeTrue());
        Navigation.Uri.ShouldBe(page);
        DialogButton(app, LeaveTitle, Localized<CommonResources>("Common.Leave.Cancel")).Click();
        app.WaitForAssertion(() => app.FindComponents<CmsDialog>().ShouldNotContain(found => found.Instance.Title == LeaveTitle));
        Navigation.Uri.ShouldBe(page);
        kept();

        var other = app.FindAll(".site-switcher option").Select(option => option.GetAttribute("value")).First(value => value != MainSite.ToString("D"));
        app.Find(".site-switcher select").Change(other);
        Dialog(app, Text("Shell.SiteSwitcher.UnsavedTitle")).Instance.Open.ShouldBeTrue();
        DialogButton(app, Text("Shell.SiteSwitcher.UnsavedTitle"), Text("Shell.SiteSwitcher.UnsavedCancel")).Click();
        Context.Site!.Id.ShouldBe(MainSite);
        kept();

        var session = (SignedInTestSession)Services.GetRequiredService<IAdminSession>();
        SignOut(app);
        Dialog(app, Text("Shell.SignOut.Title")).Instance.Open.ShouldBeTrue();
        DialogButton(app, Text("Shell.SignOut.Title"), Text("Shell.SignOut.Cancel")).Click();
        session.SignOuts.ShouldBe(0);
        kept();

        await app.InvokeAsync(() => Services.GetRequiredService<FormDrafts>().SaveTrackedAsync(session.User!.Subject));
        Storage[FormDrafts.StorageKey].ShouldBeNull();
        Storage.Writes.ShouldNotContain($"set {FormDrafts.StorageKey}");
        Storage.Items.Values.ShouldAllBe(value => typed.All(text => !value.Contains(text, StringComparison.Ordinal)));
        Services.GetRequiredService<IFormDraftStore>().HasUnsavedForms.ShouldBeTrue();
        kept();
    }

    // A confirmed leave goes on as the navigation it was, and the editor is given up.
    private protected void LeaveWhenAsked(IRenderedComponent<AdminApp> app, string route)
    {
        Navigation.NavigateTo(route);
        app.WaitForAssertion(() => Dialog(app, LeaveTitle).Instance.Open.ShouldBeTrue());
        DialogButton(app, LeaveTitle, Localized<CommonResources>("Common.Leave.Confirm")).Click();

        app.WaitForAssertion(() => Navigation.Uri.ShouldBe(Navigation.ToAbsoluteUri(route).AbsoluteUri));
        Services.GetRequiredService<IFormDraftStore>().HasUnsavedForms.ShouldBeFalse();
        UnloadGuards()[^1].ShouldBeFalse();
    }

    // An editor as it was read, or saved: the tab's close, a sign-out and a route to another page ask nothing.
    private protected void NothingAsks(IRenderedComponent<AdminApp> app, string route = "")
    {
        Services.GetRequiredService<IFormDraftStore>().HasUnsavedForms.ShouldBeFalse();
        if (UnloadGuards() is [.., var last])
        {
            last.ShouldBeFalse();
        }

        var session = (SignedInTestSession)Services.GetRequiredService<IAdminSession>();
        SignOut(app);
        session.SignOuts.ShouldBe(1);

        Navigation.NavigateTo(route);
        app.WaitForAssertion(() => Navigation.Uri.ShouldBe(Navigation.ToAbsoluteUri(route).AbsoluteUri));
        app.FindComponents<CmsDialog>().ShouldNotContain(found => found.Instance.Open);
    }

    private string LeaveTitle => Localized<CommonResources>("Common.Leave.Title");

    private List<bool> UnloadGuards() =>
        [.. Document.Invocations.Where(call => call.Identifier == "guardUnload").Select(call => (bool)call.Arguments[0]!)];

    private static IRenderedComponent<CmsDialog> Dialog(IRenderedComponent<AdminApp> app, string title) =>
        app.FindComponents<CmsDialog>().Single(found => found.Instance.Title == title);

    private static IElement DialogButton(IRenderedComponent<AdminApp> app, string title, string text) =>
        Dialog(app, title).FindAll("fluent-button").Single(button => button.TextContent.Trim() == text);

    // The answer to the site switcher's question, for a test that switches with unsaved changes.
    private protected void ConfirmSiteSwitch(IRenderedComponent<AdminApp> app) =>
        DialogButton(app, Text("Shell.SiteSwitcher.UnsavedTitle"), Text("Shell.SiteSwitcher.UnsavedConfirm")).Click();

    // The library raises the item's click from its menu's change event.
    private void SignOut(IRenderedComponent<AdminApp> app)
    {
        var item = app.FindAll("fluent-menu-item").Single(found => found.TextContent.Trim() == Text("Shell.UserMenu.SignOut"));
        item.TriggerEvent("onmenuitemchange", new MenuItemEventArgs { Id = item.Id });
    }

    internal sealed class AdministrationLoader : IAdminAssemblyLoader
    {
        public int Loads { get; private set; }

        public int Failures { get; set; }

        // A failure that is thrown before the load first waits, as a loader that refuses the name at once does.
        public bool FailsAtOnce { get; set; }

        // A load waits for it while set, so that a navigation can overtake the one that started it.
        public TaskCompletionSource? Hold { get; set; }

        // What a load of the administration assembly answers.
        public Assembly Administration { get; set; } = typeof(UsersPage).Assembly;

        public Task<Assembly> LoadAsync(string assemblyName, CancellationToken cancellationToken)
        {
            Loads++;
            if (FailsAtOnce && Failures > 0)
            {
                Failures--;
                return Task.FromException<Assembly>(new InvalidOperationException("The app does not list the assembly as lazy."));
            }

            return LoadLaterAsync(assemblyName);
        }

        private async Task<Assembly> LoadLaterAsync(string assemblyName)
        {
            await (Hold?.Task ?? Task.CompletedTask);
            await Task.Yield();
            if (Failures > 0)
            {
                Failures--;
                throw new HttpRequestException("The file did not download.");
            }

            return assemblyName == AdministrationAreas.AssemblyName ? Administration
                : assemblyName == typeof(ReviewsAdminPlugin).Assembly.GetName().Name ? typeof(ReviewsAdminPlugin).Assembly
                : throw new InvalidOperationException($"{assemblyName} is not an assembly the app lists as lazy.");
        }
    }
}
