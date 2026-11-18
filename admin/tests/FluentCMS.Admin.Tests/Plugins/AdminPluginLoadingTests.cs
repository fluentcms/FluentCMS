using System.Reflection;
using Bunit;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Fields;
using FluentCMS.Admin.Lists;
using FluentCMS.Admin.Pickers;
using FluentCMS.Admin.Plugins;
using FluentCMS.Admin.Plugins.Sample;
using FluentCMS.Admin.Shell;
using FluentCMS.Admin.Tests.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Plugins;

// The shell with the sample half of the tests as the app's one admin half: its manifest from the gathered file, and its
// assembly from a loader that counts the loads.
public sealed class AdminPluginLoadingTests : AdminTestContext
{
    private const string Permission = "reviews.notes.manage";

    private static readonly Guid Shop = new("0193a1b2-0000-7000-8000-00000000a002");

    private readonly CountingLoader loader = new();

    public AdminPluginLoadingTests() => UseNeutralResources();

    private static CancellationToken Token => Xunit.TestContext.Current.CancellationToken;

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    private AdminContext Context => Services.GetRequiredService<AdminContext>();

    private AdminPlugins Plugins => Services.GetRequiredService<AdminPlugins>();

    private static AdminPluginManifest Sample => AdminPluginManifestJson.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, AdminPluginManifest.FileName)));

    [Fact]
    public void TheSidebarShowsTheHalfsMenuItemWithTheLabelOfItsManifestWithoutLoadingIt()
    {
        var app = Load(enabled: true);

        var link = app.Find(".shell-sidebar a.cms-nav-link[href='reviews']");
        link.TextContent.Trim().ShouldBe("Reviews");
        loader.Loads.ShouldBe(0);
    }

    [Fact]
    public void TheMenuItemIsAbsentWhileThePluginIsDisabledOrTheUserLacksThePermission()
    {
        var app = Load(enabled: false);

        app.FindAll(".shell-sidebar a.cms-nav-link[href='reviews']").ShouldBeEmpty();
    }

    [Fact]
    public void WithoutThePermissionTheMenuItemIsAbsentAndThePageAnswersNoAccessWithoutLoadingTheHalf()
    {
        var app = Load(enabled: true, route: "reviews", permitted: false);

        app.FindAll(".shell-sidebar a.cms-nav-link[href='reviews']").ShouldBeEmpty();
        app.WaitForAssertion(() => app.Find("main h1").TextContent.ShouldBe("No access"));
        loader.Loads.ShouldBe(0);
    }

    [Fact]
    public void TheFirstVisitOfAPageLoadsTheHalfOnceAndRunsConfigureOnceIntoAProviderOfItsOwn()
    {
        var app = Load(enabled: true, route: "reviews");

        app.WaitForAssertion(() => app.Find("main .cms-card p").TextContent.ShouldBe("0"));
        Navigation.NavigateTo("");
        Navigation.NavigateTo("reviews");
        app.WaitForAssertion(() => app.Find("main .cms-card p").TextContent.ShouldBe("0"));

        loader.Loads.ShouldBe(1);
        Plugins.Assemblies.ShouldBe([typeof(ReviewsAdminPlugin).Assembly]);
        var provider = Plugins.ProviderOf(typeof(ReviewsPage));
        provider.GetService<ReviewsApi>().ShouldNotBeNull().ShouldBeSameAs(provider.GetService<ReviewsApi>());
        Services.GetService<ReviewsApi>().ShouldBeNull();
        provider.GetService<IAdminContext>().ShouldBeSameAs(Context);
        provider.GetRequiredService<IOptions<ManagementApiOptions>>().Value.ApiBaseUrl
            .ShouldBe(Services.GetRequiredService<IOptions<ManagementApiOptions>>().Value.ApiBaseUrl);
        provider.GetServices<IConfigureOptions<ManagementApiOptions>>().Count()
            .ShouldBe(Services.GetServices<IConfigureOptions<ManagementApiOptions>>().Count());
    }

    [Fact]
    public void InASiteWhereThePluginIsDisabledThePageIsNotFoundAndTheHalfIsNeverLoaded()
    {
        var app = Load(enabled: false, route: "reviews");

        app.WaitForAssertion(() => app.Find("main h1").TextContent.ShouldBe("Page not found"));
        loader.Loads.ShouldBe(0);
    }

    [Fact]
    public void AHalfLoadedInOneSiteShowsNothingInASiteWhereItsPluginIsDisabled()
    {
        Management.AddSite(Shop, "Shop", "shop");
        Management.Grant(Shop, ["schema.read", Permission]);
        var app = Load(enabled: true, route: "reviews");
        app.WaitForAssertion(() => app.Find("main .cms-card"));

        app.Find(".site-switcher select").Change(Shop.ToString("D"));

        app.WaitForAssertion(() => Navigation.Uri.ShouldBe(Navigation.BaseUri));
        app.FindAll(".shell-sidebar a.cms-nav-link[href='reviews']").ShouldBeEmpty();
        Plugins.Gate("reviews").ShouldBe(PluginPageGate.NotFound);
    }

    [Fact]
    public void AfterASwitchToASiteWhereTheUserLacksThePermissionThePageAnswersNoAccess()
    {
        Management.AddSite(Shop, "Shop", "shop");
        Management.Plugins[Shop] = [Plugin("reviews", enabled: true)];
        var app = Load(enabled: true, route: "reviews");
        app.WaitForAssertion(() => app.Find("main .cms-card"));

        app.Find(".site-switcher select").Change(Shop.ToString("D"));

        app.WaitForAssertion(() => app.Find("main h1").TextContent.ShouldBe("No access"));
        Navigation.Uri.ShouldEndWith("/reviews");
    }

    [Fact]
    public void AfterASwitchToASiteWhereThePluginIsEnabledItsPageLoadsTheHalf()
    {
        Management.AddSite(Shop, "Shop", "shop", isDefault: true);
        Management.Grant(Shop, ["schema.read", Permission]);
        Management.Plugins[Shop] = [Plugin("reviews", enabled: true)];
        var app = Load(enabled: false, route: "reviews");
        app.WaitForAssertion(() => app.Find("main h1").TextContent.ShouldBe("Page not found"));
        loader.Loads.ShouldBe(0);

        app.Find(".site-switcher select").Change(Shop.ToString("D"));

        app.WaitForAssertion(() => app.Find("main .cms-card p").TextContent.ShouldBe("0"));
        loader.Loads.ShouldBe(1);
    }

    [Fact]
    public async Task TheRegistriesAnswerAFieldTypeOfTheHalfOnceTheyHaveLoadedIt()
    {
        Load(enabled: true);
        var editors = Services.GetRequiredService<FieldEditorRegistry>();
        var cells = Services.GetRequiredService<ListCellRegistry>();
        var filters = Services.GetRequiredService<ListFilterRegistry>();
        var rating = new SchemaFieldDescription { Name = "stars", Type = "rating", Validation = new(), Settings = [] };

        editors.Find("rating").ShouldBeNull();
        cells.CellFor("rating").ShouldBe(typeof(TextListCell));
        cells.Shows(rating).ShouldBeTrue();
        filters.Find(rating).ShouldBeNull();
        loader.Loads.ShouldBe(0);

        (await editors.FindAsync("rating", Token)).ShouldNotBeNull().Editor.ShouldBe(typeof(RatingEditor));
        (await cells.CellForAsync("rating", Token)).ShouldBe(typeof(RatingCell));
        (await filters.FindAsync(rating, Token)).ShouldNotBeNull().Operators.ShouldBe([FilterOperator.Equal, FilterOperator.GreaterThan]);
        editors.Find("rating").ShouldNotBeNull().Editor.ShouldBe(typeof(RatingEditor));
        editors.Find("text").ShouldNotBeNull().Editor.ShouldBe(typeof(TextFieldEditor));
        loader.Loads.ShouldBe(1);
    }

    [Fact]
    public async Task WhileThePluginIsDisabledTheRegistriesKnowNoneOfItsFieldTypesAndLoadNothing()
    {
        Load(enabled: false);
        var rating = new SchemaFieldDescription { Name = "stars", Type = "rating", Validation = new(), Settings = [] };

        (await Services.GetRequiredService<FieldEditorRegistry>().FindAsync("rating", Token)).ShouldBeNull();
        (await Services.GetRequiredService<ListCellRegistry>().CellForAsync("rating", Token)).ShouldBe(typeof(TextListCell));
        Services.GetRequiredService<ListCellRegistry>().Shows(rating).ShouldBeFalse();
        (await Services.GetRequiredService<ListFilterRegistry>().FindAsync(rating, Token)).ShouldBeNull();
        loader.Loads.ShouldBe(0);
    }

    [Fact]
    public async Task AServiceOfAHalfIsFoundByItsInterfaceAndLoadsTheHalf()
    {
        Load(enabled: true);
        var services = Services.GetRequiredService<IAdminPluginServices>();

        var picker = await services.FindAsync<IMediaPicker>(Token);

        picker.ShouldBeOfType<SampleMediaPicker>();
        (await services.FindAsync<IMediaPicker>(Token)).ShouldBeSameAs(picker);
        (await services.FindAsync<IRelationPicker>(Token)).ShouldBeNull();
        loader.Loads.ShouldBe(1);
    }

    [Fact]
    public async Task NoServiceIsFoundOfADisabledPlugin()
    {
        Load(enabled: false);

        (await Services.GetRequiredService<IAdminPluginServices>().FindAsync<IMediaPicker>(Token)).ShouldBeNull();
        loader.Loads.ShouldBe(0);
    }

    [Fact]
    public void AHalfThatFailsToLoadShowsAnErrorWithARetryThatLoadsItAgain()
    {
        loader.Failures = 1;

        var app = Load(enabled: true, route: "reviews");

        app.WaitForAssertion(() => app.Find("main h1").TextContent.ShouldBe("This page could not load"));
        app.FindAll("main fluent-button").Single(button => button.TextContent.Trim() == "Try again").Click();
        app.WaitForAssertion(() => app.Find("main .cms-card p").TextContent.ShouldBe("0"));
        loader.Loads.ShouldBe(2);
    }

    [Fact]
    public void ProviderOfATypeOutsideALoadedHalfFails()
    {
        Load(enabled: true);

        Should.Throw<InvalidOperationException>(() => Plugins.ProviderOf(typeof(ReviewsPage)));
        Should.Throw<InvalidOperationException>(() => Plugins.ProviderOf(typeof(AdminApp)));
    }

    [Fact]
    public void AHalfOutsideTheAdminRangeIsListedOnTheErrorPageAndTheRestOfTheAdminWorks()
    {
        var later = Sample with { Name = "later", AdminRange = "[2.0.0,3.0.0)" };
        Services.AddSingleton(new AdminPluginCatalog([], [new PluginProblem(PluginProblemKind.OutOfRange, later.Name, later.Assembly, later.AdminRange)]));

        var app = Load(enabled: true, catalog: false);
        app.Find("main a[href='system/plugins']").TextContent.ShouldBe("Some plugin admin parts of this admin are not loaded.");
        Navigation.NavigateTo("system/plugins");

        app.WaitForAssertion(() => app.Find("main li").TextContent.ShouldBe(
            $"later (FluentCMS.Admin.Plugins.Sample): it was built for the admin versions [2.0.0,3.0.0), and this admin is {AdminPluginCatalog.RunningVersion.ToString(3)}."));
        app.FindAll(".shell-sidebar a.cms-nav-link").ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("reviews", "FluentCMS.Admin.Plugins.Sample.ReviewsPage")]
    [InlineData("reviews/new", "FluentCMS.Admin.Plugins.Sample.ReviewPage")]
    [InlineData("Reviews/0193a1b2-0000-7000-8000-00000000a001?tab=1", "FluentCMS.Admin.Plugins.Sample.ReviewPage")]
    [InlineData("reviews/a/b", null)]
    public void ARouteMatchesThePageOfItsTemplate(string route, string? type) =>
        AdminPluginRoutes.Match(Sample.Pages, route)?.Type.ShouldBe(type);

    [Fact]
    public void TheMoreLiteralTemplateWins() =>
        AdminPluginRoutes.Match(Sample.Pages, "reviews/new")!.Permission.ShouldBe("reviews.notes.create");

    private IRenderedComponent<AdminApp> Load(bool enabled, string route = "", bool catalog = true, bool permitted = true)
    {
        if (catalog)
        {
            Services.AddSingleton(new AdminPluginCatalog([Sample], []));
        }

        Services.AddScoped<IAdminAssemblyLoader>(_ => loader);
        Management.Grant(MainSite, permitted ? ["schema.read", Permission] : ["schema.read"]);
        Management.Plugins[MainSite] = [Plugin("reviews", enabled)];
        Start();
        Navigation.NavigateTo(route);
        var app = Render<AdminApp>();
        app.WaitForAssertion(() => Context.Status.ShouldBe(ShellStatus.Ready));
        return app;
    }

    // The assembly of the sample half is in the test's process already, so a load only counts, or fails as often as asked.
    private sealed class CountingLoader : IAdminAssemblyLoader
    {
        public int Loads { get; private set; }

        public int Failures { get; set; }

        public async Task<Assembly> LoadAsync(string assemblyName, CancellationToken cancellationToken)
        {
            Loads++;
            await Task.Yield();
            if (Failures > 0)
            {
                Failures--;
                throw new HttpRequestException("The half's file did not download.");
            }

            assemblyName.ShouldBe("FluentCMS.Admin.Plugins.Sample");
            return typeof(ReviewsAdminPlugin).Assembly;
        }
    }
}
