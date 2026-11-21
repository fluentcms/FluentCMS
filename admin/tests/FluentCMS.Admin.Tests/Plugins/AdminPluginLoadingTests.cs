using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using Bunit;
using Bunit.TestDoubles;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Components;
using FluentCMS.Admin.Fields;
using FluentCMS.Admin.Lists;
using FluentCMS.Admin.Pickers;
using FluentCMS.Admin.Plugins;
using FluentCMS.Admin.Plugins.Sample;
using FluentCMS.Admin.Shell;
using FluentCMS.Admin.Tests.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    private readonly CapturingLogger log = new();

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
    public void ADeepLinkWhoseFirstReadOfThePluginsFailedShowsThePageOnceARetryReadsThemWithoutANavigation()
    {
        var failing = true;
        Management.Answer = (method, path) => failing && path == "plugins" ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : null;
        var app = Load(enabled: true, route: "reviews", ready: false);
        app.WaitForAssertion(() => Context.Status.ShouldBe(ShellStatus.Failed));
        loader.Loads.ShouldBe(0);
        failing = false;

        app.FindAll("main fluent-button").Single(button => button.TextContent.Trim() == "Try again").Click();

        app.WaitForAssertion(() => app.Find("main .cms-card p").TextContent.ShouldBe("0"), TimeSpan.FromSeconds(10));
        var history = Services.GetRequiredService<BunitNavigationManager>().History;
        history.Where(entry => entry.Options.ReplaceHistoryEntry).ShouldHaveSingleItem().Uri.ShouldEndWith("reviews");
        Document.Invocations.Count(call => call.Identifier == "focusHeading").ShouldBe(0);
        loader.Loads.ShouldBe(1);
    }

    [Fact]
    public async Task APageAnsweredNoAccessIsShownWhenALocaleWhoseAccessGrantsItIsChosenWhileTheShellStaysReady()
    {
        Management.AddLocale(MainSite, "fr", 1);
        Management.Grant(MainSite, ["schema.read", Permission]);
        Management.LocaleAccess[(MainSite, "fr")] = Management.Access[MainSite];
        var app = Load(enabled: true, route: "reviews", permitted: false);
        app.WaitForAssertion(() => app.Find("main h1").TextContent.ShouldBe("No access"));

        await app.InvokeAsync(() => Context.UseLocaleAsync("fr"));

        app.WaitForAssertion(() => app.Find("main .cms-card p").TextContent.ShouldBe("0"), TimeSpan.FromSeconds(10));
        Context.Status.ShouldBe(ShellStatus.Ready);
        Document.Invocations.Count(call => call.Identifier == "focusHeading").ShouldBe(0);
        loader.Loads.ShouldBe(1);
    }

    [Fact]
    public async Task ADeepLinkAnsweredDuringASiteSwitchMadeOutsideTheSwitcherShowsThePageOnceTheSwitchHasRead()
    {
        Management.AddSite(Shop, "Shop", "shop");
        Management.Grant(Shop, ["schema.read", Permission]);
        Management.Plugins[Shop] = [Plugin("reviews", enabled: true)];
        var app = Load(enabled: false);
        var held = Management.HoldSite[Shop] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task switching = null!;
        await app.InvokeAsync(() => { switching = Context.SwitchSiteAsync(Shop); });
        await app.InvokeAsync(() => Navigation.NavigateTo("reviews"));
        Plugins.Gate("reviews").ShouldBe(PluginPageGate.NotFound);
        loader.Loads.ShouldBe(0);

        held.SetResult();

        app.WaitForAssertion(() => app.Find("main .cms-card p").TextContent.ShouldBe("0"), TimeSpan.FromSeconds(10));
        switching.IsCompletedSuccessfully.ShouldBeTrue();
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
    public async Task TwoFirstUsesAtOnceLoadTheHalfAndRunConfigureOnce()
    {
        loader.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var app = Load(enabled: true);

        // On the renderer's dispatcher, as a component asks, so that no wait of the test holds their continuations.
        Task<IMediaPicker?> picker = null!;
        Task<FieldEditorRegistration?> editor = null!;
        await app.InvokeAsync(() =>
        {
            picker = Services.GetRequiredService<IAdminPluginServices>().FindAsync<IMediaPicker>(Token).AsTask();
            editor = Services.GetRequiredService<FieldEditorRegistry>().FindAsync("rating", Token).AsTask();
        });
        Navigation.NavigateTo("reviews");
        loader.Gate.SetResult();

        (await picker).ShouldBeOfType<SampleMediaPicker>();
        (await editor).ShouldNotBeNull();
        app.WaitForAssertion(() => app.Find("main .cms-card p").TextContent.ShouldBe("0"));
        loader.Loads.ShouldBe(1);
        ReviewsAdminPlugin.ConfiguredFor(Services.GetRequiredService<ListFilterRegistry>()).ShouldBe(1);
    }

    [Fact]
    public async Task AFailedLoadIsKeptUntilTheNextNavigationAndAServiceOfTheHalfThenThrows()
    {
        loader.Failures = 1;
        Load(enabled: true);
        var services = Services.GetRequiredService<IAdminPluginServices>();
        var editors = Services.GetRequiredService<FieldEditorRegistry>();

        await Should.ThrowAsync<InvalidOperationException>(services.FindAsync<IMediaPicker>(Token).AsTask());
        await Should.ThrowAsync<HttpRequestException>(editors.FindAsync("rating", Token).AsTask());
        await Should.ThrowAsync<InvalidOperationException>(services.FindAsync<IMediaPicker>(Token).AsTask());
        loader.Loads.ShouldBe(1);

        Navigation.NavigateTo("system/components");

        (await services.FindAsync<IMediaPicker>(Token)).ShouldBeOfType<SampleMediaPicker>();
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

    [Fact]
    public void APageTheRendererCannotCreateShowsAnErrorInTheLayoutAndTheRestOfTheAdminWorks()
    {
        var app = Load(enabled: true, route: "reviews/misinjected");

        app.WaitForAssertion(() => app.Find("main h1").TextContent.ShouldBe("This page could not be shown"));
        app.Find(".shell-sidebar a.cms-nav-link[href='reviews']");
        NotCreated().ShouldBe([$"{typeof(MisinjectedPage).FullName}.Api"]);

        Navigation.NavigateTo("reviews");

        app.WaitForAssertion(() => app.Find("main .cms-card p").TextContent.ShouldBe("0"));
    }

    [Fact]
    public void AFieldEditorTheRendererCannotCreateGivesWayToTheReadOnlyValueAndTheOtherFieldsAreEdited()
    {
        Management.SetTypes(MainSite, Type("note", fields: [Field("title"), Field("stars", "rating")]));
        var app = Load(enabled: true, route: "content/note/new", manifest: Sample with { FieldEditors = [new() { Type = typeof(MisinjectedEditor).FullName!, FieldType = "rating" }] });

        app.WaitForAssertion(() => app.Find("[data-field='stars'] .content-json"));
        app.FindAll("[data-field='stars'] input").ShouldBeEmpty();
        app.Find("[data-field='title'] input").Input("Hello");
        app.WaitForAssertion(() => app.Find("[data-field='title'] input").GetAttribute("value").ShouldBe("Hello"));
        NotCreated().ShouldBe([$"{typeof(MisinjectedEditor).FullName}.Api"]);
    }

    [Fact]
    public void AListCellTheRendererCannotCreateGivesWayToTheValueAsTextAndTheListWorks()
    {
        Management.SetTypes(MainSite, Type("note", fields: [Field("title"), Field("stars", "rating")]));
        Management.Entries["note"] = [new JsonObject { ["id"] = "0193a1b2-0000-7000-8000-000000000001", ["status"] = "draft", ["locale"] = "en", ["version"] = 1, ["title"] = "Hello", ["stars"] = 4 }];
        var app = Load(enabled: true, route: "content/note?columns=title,stars", manifest: Sample with { ListCells = [new() { Type = typeof(MisinjectedCell).FullName!, FieldType = "rating" }] });

        app.WaitForAssertion(() => app.FindAll("main tbody .cms-list-cell-text").Select(cell => cell.TextContent).ShouldBe(["4"]));
        app.Find("main tbody").TextContent.ShouldContain("Hello");
        app.FindAll("fluent-button").First(button => button.TextContent.Trim() == "Add filter").Click();
        app.WaitForAssertion(() => app.FindComponents<CmsSelect>().Single(input => input.Instance.Label == "Field").Instance.Options.Select(option => option.Value).ShouldContain("stars"));
        NotCreated().ShouldBe([$"{typeof(MisinjectedCell).FullName}.Api"]);
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

    // The property each logged problem of a class the renderer cannot create names, once it points to ProviderOf.
    private List<string> NotCreated() =>
    [
        .. log.Entries.Where(entry => entry.Event == "PluginComponentNotCreated").Select(entry =>
        {
            entry.Message.ShouldContain("IAdminPluginServices.ProviderOf");
            var start = entry.Message.IndexOf(": ", StringComparison.Ordinal) + 2;
            return entry.Message[start..entry.Message.IndexOf(' ', start)];
        }),
    ];

    private IRenderedComponent<AdminApp> Load(bool enabled, string route = "", bool catalog = true, bool permitted = true, bool ready = true, AdminPluginManifest? manifest = null)
    {
        if (catalog)
        {
            Services.AddSingleton(new AdminPluginCatalog([manifest ?? Sample], []));
        }

        Services.AddSingleton<ILogger<AdminPlugins>>(log);
        Services.AddScoped<IAdminAssemblyLoader>(_ => loader);
        Management.Grant(MainSite, permitted ? ["schema.read", Permission] : ["schema.read"]);
        Management.Plugins[MainSite] = [Plugin("reviews", enabled)];
        Start();
        Navigation.NavigateTo(route);
        var app = Render<AdminApp>();
        if (ready)
        {
            app.WaitForAssertion(() => Context.Status.ShouldBe(ShellStatus.Ready));
        }

        return app;
    }

    private sealed class CapturingLogger : ILogger<AdminPlugins>
    {
        public List<(string? Event, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Entries)
            {
                Entries.Add((eventId.Name, formatter(state, exception)));
            }
        }
    }

    // The assembly of the sample half is in the test's process already, so a load only counts, or fails as often as asked.
    private sealed class CountingLoader : IAdminAssemblyLoader
    {
        public int Loads { get; private set; }

        public int Failures { get; set; }

        // A load waits for it while set, so that several uses meet one load in progress.
        public TaskCompletionSource? Gate { get; set; }

        public async Task<Assembly> LoadAsync(string assemblyName, CancellationToken cancellationToken)
        {
            Loads++;
            await (Gate?.Task ?? Task.CompletedTask);
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
