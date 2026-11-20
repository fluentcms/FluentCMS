using System.Reflection;
using System.Text.Json.Nodes;
using Bunit;
using FluentCMS.Admin.Administration.Users;
using FluentCMS.Admin.Plugins;
using FluentCMS.Admin.Plugins.Sample;
using FluentCMS.Admin.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Administration;

// The shell with the administration assembly behind a loader that counts its loads, since the assembly is in the test's
// process already. A test of an area derives from it, in a folder of its own beside this file.
public abstract class AdministrationTestContext : AdminTestContext
{
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

    private protected static string Heading(IRenderedComponent<AdminApp> app) => app.Find("main h1").TextContent;

    private protected static string[] Links(IRenderedComponent<AdminApp> app) =>
        [.. app.Find(".shell-sidebar").QuerySelectorAll("a.cms-nav-link").Select(link => link.GetAttribute("href")!)];

    internal sealed class AdministrationLoader : IAdminAssemblyLoader
    {
        public int Loads { get; private set; }

        public int Failures { get; set; }

        // What a load of the administration assembly answers.
        public Assembly Administration { get; set; } = typeof(UsersPage).Assembly;

        public async Task<Assembly> LoadAsync(string assemblyName, CancellationToken cancellationToken)
        {
            Loads++;
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
