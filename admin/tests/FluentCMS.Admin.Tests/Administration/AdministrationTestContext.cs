using System.Reflection;
using System.Text.Json.Nodes;
using Bunit;
using FluentCMS.Admin.Administration.Users;
using FluentCMS.Admin.Plugins;
using FluentCMS.Admin.Plugins.Sample;
using FluentCMS.Admin.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
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

    private protected static string[] Links(IRenderedComponent<AdminApp> app) =>
        [.. app.Find(".shell-sidebar").QuerySelectorAll("a.cms-nav-link").Select(link => link.GetAttribute("href")!)];

    // The first route of the page of the administration assembly that the router shows, or none while it shows another.
    private protected static string? Shown(IRenderedComponent<AdminApp> app)
    {
        var page = app.FindComponent<RouteView>().Instance.RouteData.PageType;
        return page.Assembly == typeof(UsersPage).Assembly ? page.GetCustomAttributes<RouteAttribute>().First().Template : null;
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
