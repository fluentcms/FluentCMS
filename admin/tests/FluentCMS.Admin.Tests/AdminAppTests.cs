using Bunit;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Components;
using FluentCMS.Admin.Configuration;
using FluentCMS.Admin.Shell;
using FluentCMS.Admin.Tests.Configuration;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests;

public sealed class AdminAppTests : AdminTestContext
{
    private readonly FakeLogger<AdminApp> _logger = new();

    public AdminAppTests()
    {
        UseNeutralResources();
        Services.AddSingleton<ILogger<AdminApp>>(_logger);
    }

    [Fact]
    public async Task AValidConfigurationStartsTheAppAndIsRegistered()
    {
        var handler = new FakeHandler("""{ "apiBaseUrl": "https://cms.example.com" }""");
        await Start(handler);

        var app = Render<AdminApp>();

        app.Find("main#main h1").TextContent.ShouldBe("FluentCMS admin");
        app.FindAll("main.fluentcms-configuration-error").ShouldBeEmpty();
        Services.GetRequiredService<AdminConfiguration>().ApiBaseUrl.ShouldBe(new Uri("https://cms.example.com/"));
        handler.Requests.ShouldHaveSingleItem().RequestUri.ShouldBe(new Uri(FakeHandler.AppBase, "config.json"));
    }

    [Theory]
    [InlineData("""{ "clientId": "fluentcms-admin" }""", "apiBaseUrl is required.")]
    [InlineData("""{ "apiBaseUrl": "cms.example.com" }""", "apiBaseUrl must be an absolute http or https URL")]
    public async Task AMissingOrInvalidApiBaseUrlShowsTheErrorPageWithoutARequestToTheApi(string json, string text)
    {
        var handler = new FakeHandler(json);
        await Start(handler);
        var head = Render<HeadOutlet>();

        var app = Render<AdminApp>();

        app.Find("h1").TextContent.ShouldBe("The admin cannot start");
        head.Find("title").TextContent.ShouldBe("The admin cannot start");
        app.Find("li[data-member='apiBaseUrl']").TextContent.ShouldStartWith(text);
        app.FindAll("main#main").ShouldBeEmpty();
        Services.GetService<AdminConfiguration>().ShouldBeNull();
        handler.Requests.ShouldHaveSingleItem().RequestUri.ShouldBe(new Uri(FakeHandler.AppBase, "config.json"));
    }

    [Fact]
    public async Task AMissingFileShowsTheErrorPage()
    {
        await Start(new FakeHandler(null));

        var app = Render<AdminApp>();

        app.Find("li").TextContent.ShouldBe("config.json could not be read: the server answered with status 404.");
    }

    [Fact]
    public async Task AnUnknownMemberLogsAWarning()
    {
        await Start(new FakeHandler("""{ "apiBaseUrl": "https://cms.example.com", "theme": "dark", "branding": { "slogan": "x" } }"""));

        Render<AdminApp>();

        _logger.Entries.ShouldBe([(LogLevel.Warning, "UnknownConfigurationMember", "theme"), (LogLevel.Warning, "UnknownConfigurationMember", "branding.slogan")]);
    }

    [Fact]
    public async Task PlainHttpOnARemoteHostLogsAWarning()
    {
        await Start(new FakeHandler("""{ "apiBaseUrl": "http://cms.example.com", "authority": "http://localhost:5000" }"""));

        Render<AdminApp>();

        _logger.Entries.ShouldBe([(LogLevel.Warning, "PlainHttpConfigurationMember", "apiBaseUrl")]);
    }

    [Fact]
    public async Task TheBrandingSetsTheTitleFaviconAccentColorAndLogo()
    {
        await Start(new FakeHandler("""
            { "apiBaseUrl": "https://cms.example.com",
              "branding": { "name": "Acme", "logoUrl": "img/logo.svg", "faviconUrl": "favicon.png", "accentColor": "#0f6cbd" } }
            """));
        var head = Render<HeadOutlet>();

        var app = Render<AdminApp>();

        head.Find("title").TextContent.ShouldBe("Acme");
        head.Find("link[rel='icon']").GetAttribute("href").ShouldBe("favicon.png");
        Document.Invocations["setProperty"].Select(call => call.Arguments.ToArray()).ShouldBe([[BrandingHead.AccentProperty, "#0f6cbd"]]);
        var logo = app.Find(".cms-root header a.shell-home img.cms-logo");
        logo.GetAttribute("src").ShouldBe("img/logo.svg");
        logo.GetAttribute("alt").ShouldBe("Acme, home");
    }

    [Fact]
    public async Task WithoutBrandingNoCustomPropertyIsSetAndTheShellShowsTheFluentCmsLogo()
    {
        await Start(new FakeHandler("""{ "apiBaseUrl": "https://cms.example.com" }"""));
        var head = Render<HeadOutlet>();

        var app = Render<AdminApp>();

        head.Find("title").TextContent.ShouldBe("FluentCMS");
        head.FindAll("link").ShouldBeEmpty();
        Document.Invocations["setProperty"].ShouldBeEmpty();
        app.Find("header a.shell-home .cms-logo").GetAttribute("aria-label").ShouldBe("FluentCMS, home");
    }

    [Fact]
    public async Task OneThemeRootHoldsTheLayoutAndTheLiveRegions()
    {
        await Start(new FakeHandler("""{ "apiBaseUrl": "https://cms.example.com" }"""));

        var app = Render<AdminApp>();

        app.FindComponents<CmsThemeRoot>().ShouldHaveSingleItem();
        var root = app.Find(".cms-root");
        app.FindAll(".cms-root").ShouldHaveSingleItem();
        root.QuerySelector("main#main").ShouldNotBeNull();
        root.QuerySelector("a.skip-link").ShouldNotBeNull();
        root.QuerySelectorAll("[aria-live]").Select(region => region.ClassName).ShouldBe(["cms-visually-hidden", "cms-visually-hidden"]);
        app.Nodes.OfType<AngleSharp.Dom.IElement>().ShouldHaveSingleItem().ClassName.ShouldBe("cms-root");
    }

    [Fact]
    public async Task TheThemeChosenInTheShellAppliesToTheThemeRoot()
    {
        await Start(new FakeHandler("""{ "apiBaseUrl": "https://cms.example.com" }"""));
        var app = Render<AdminApp>();
        app.Find(".cms-root").HasAttribute("data-cms-theme").ShouldBeFalse();

        await app.InvokeAsync(() => Services.GetRequiredService<ShellTheme>().Choose(CmsThemeMode.Dark));

        app.WaitForAssertion(() => app.Find(".cms-root").GetAttribute("data-cms-theme").ShouldBe("dark"));
    }

    [Fact]
    public async Task TheErrorPageHasTheTokensButNoThemeRootOrWrappedServices()
    {
        await Start(new FakeHandler("{}"));

        var app = Render<AdminApp>();

        app.FindComponents<CmsThemeRoot>().ShouldBeEmpty();
        app.Find(".cms-root main.fluentcms-configuration-error").ShouldNotBeNull();
        Services.GetService<ICmsToastService>().ShouldBeNull();
    }

    // Composes the services as AddFluentCmsAdmin does. The client stays open and is registered for every service that takes an
    // HttpClient, and the fake is the primary handler of the Management API's pipeline, so that a request to the API after
    // start goes through the fake handler and shows in its requests.
    private async Task Start(FakeHandler handler)
    {
        var http = handler.CreateClient();
        var startup = await AdminConfigurationLoader.LoadAsync(http, Xunit.TestContext.Current.CancellationToken);
        Services.AddSingleton(http);
        Start(startup);
        Services.Configure<ManagementApiOptions>(options => options.PrimaryHandler = () => handler);
    }

    private sealed class FakeLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string? Event, string Member)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = (IReadOnlyList<KeyValuePair<string, object?>>)state!;
            Entries.Add((logLevel, eventId.Name, (string)values.Single(value => value.Key == "Member").Value!));
        }
    }
}
