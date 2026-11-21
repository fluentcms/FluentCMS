using System.Net;
using Bunit;
using FluentCMS.Admin.Interop;
using FluentCMS.Admin.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Shell;

// AdminContext.ReadSitesAsync, which a page calls after a change of a site or its locales (spec 4.16), so that the site
// switcher and the locale switcher follow without a reload.
public sealed class ReadSitesTests : AdminTestContext
{
    private static readonly Guid Shop = new("0193a1b2-0000-7000-8000-00000000a002");

    public ReadSitesTests() => UseNeutralResources();

    private AdminContext Context => Services.GetRequiredService<AdminContext>();

    [Fact]
    public async Task TheSiteShownTakesItsNewNameAndLocalesAndTheSwitcherFollows()
    {
        Management.AddSite(Shop, "Shop", "shop");
        var app = Load();
        var changes = 0;
        Context.Changed += () => changes++;
        Management.Sites[0]!["name"] = "Main street";
        Management.AddLocale(MainSite, "fr", 1);
        Management.Requests.Clear();

        await app.InvokeAsync(Context.ReadSitesAsync);

        Context.Site!.Name.ShouldBe("Main street");
        Context.ContentLocales.ShouldBe(["en", "fr"]);
        changes.ShouldBe(1);
        // The site is not read again; the sidebar reads the access of each enabled locale once there are two.
        Management.Requests[0].ShouldBe(("me/sites", null));
        Management.Requests.ShouldNotContain(request => request.Path == "schema" || request.Path == "plugins");
        app.WaitForAssertion(() => app.Find(".site-switcher select").QuerySelectorAll("option").Select(option => option.TextContent).ShouldContain("Main street"));
    }

    [Fact]
    public async Task AContentLocaleTheSiteNoLongerHasEnabledGoesBackToTheDefaultAndItsAccessIsReadAgain()
    {
        Management.AddLocale(MainSite, "fr", 1);
        var app = Load();
        await app.InvokeAsync(() => Context.UseLocaleAsync("fr"));
        Context.ContentLocale.ShouldBe("fr");
        Management.Sites[0]!["locales"]!.AsArray()[1]!["enabled"] = false;
        Management.MetadataLocales.Clear();

        await app.InvokeAsync(Context.ReadSitesAsync);

        Context.ContentLocale.ShouldBe("en");
        Context.NamedLocale.ShouldBeNull();
        Context.AccessCurrent.ShouldBeTrue();
        Management.MetadataLocales.ShouldContain(("me/access", null));
    }

    [Fact]
    public async Task ANewDefaultLocaleReadsTheAccessOfTheSiteAgain()
    {
        Management.AddLocale(MainSite, "fr", 1);
        var app = Load();
        Management.Sites[0]!["defaultLocale"] = "fr";
        Management.Requests.Clear();

        await app.InvokeAsync(Context.ReadSitesAsync);

        Context.ContentLocale.ShouldBe("fr");
        Management.Requests.ShouldContain(("me/access", MainSite.ToString("D")));
        Context.Status.ShouldBe(ShellStatus.Ready);
    }

    // A deleted site, as the multi-site half deletes the one the shell shows.
    [Fact]
    public async Task WhenTheSiteShownIsGoneTheShellMovesToTheDefaultSite()
    {
        Management.AddSite(Shop, "Shop", "shop");
        await Storage.SetAsync(TabStorageKeys.Site, Shop.ToString("D"));
        var app = Load();
        Context.Site!.Id.ShouldBe(Shop);
        Management.Sites.RemoveAt(1);
        Management.Requests.Clear();

        await app.InvokeAsync(Context.ReadSitesAsync);

        Context.Site!.Id.ShouldBe(MainSite);
        Context.Status.ShouldBe(ShellStatus.Ready);
        Context.Sites.ShouldHaveSingleItem();
        Storage[TabStorageKeys.Site].ShouldBe(MainSite.ToString("D"));
        Management.Requests.ShouldContain(("me/access", MainSite.ToString("D")));
        Management.Requests.ShouldNotContain(request => request.Site == Shop.ToString("D"));
    }

    [Fact]
    public async Task AReadThatFailsChangesNothing()
    {
        var app = Load();
        Management.Sites[0]!["name"] = "Main street";
        Management.Answer = (method, path) => path == "me/sites" ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : null;

        await app.InvokeAsync(Context.ReadSitesAsync);

        Context.Site!.Name.ShouldBe("Main");
        Context.Status.ShouldBe(ShellStatus.Ready);
    }

    private IRenderedComponent<AdminApp> Load(string route = "")
    {
        Start();
        Services.GetRequiredService<NavigationManager>().NavigateTo(route);
        var app = Render<AdminApp>();
        app.WaitForAssertion(() => Context.Status.ShouldBe(ShellStatus.Ready));
        return app;
    }
}
