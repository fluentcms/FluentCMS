using Bunit;
using FluentCMS.Admin.Configuration;
using FluentCMS.Admin.Tests.Configuration;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests;

public sealed class AdminAppTests : BunitContext
{
    private readonly FakeLogger<AdminApp> _logger = new();

    public AdminAppTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ILogger<AdminApp>>(_logger);
    }

    [Fact]
    public async Task AValidConfigurationStartsTheAppAndIsRegistered()
    {
        var handler = new FakeHandler("""{ "apiBaseUrl": "https://cms.example.com" }""");
        await Start(handler);

        var app = Render<AdminApp>();

        app.Markup.ShouldBeEmpty();
        Services.GetRequiredService<AdminConfiguration>().ApiBaseUrl.ShouldBe(new Uri("https://cms.example.com/"));
        handler.Requests.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("""{ "clientId": "fluentcms-admin" }""", "apiBaseUrl is required.")]
    [InlineData("""{ "apiBaseUrl": "cms.example.com" }""", "apiBaseUrl must be an absolute http or https URL")]
    public async Task AMissingOrInvalidApiBaseUrlShowsTheErrorPageWithoutARequestToTheApi(string json, string text)
    {
        var handler = new FakeHandler(json);
        await Start(handler);

        var app = Render<AdminApp>();

        app.Find("h1").TextContent.ShouldBe(ConfigurationTexts.Title);
        app.Find("li[data-member='apiBaseUrl']").TextContent.ShouldStartWith(text);
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

        _logger.Entries.ShouldBe([(LogLevel.Warning, "theme"), (LogLevel.Warning, "branding.slogan")]);
    }

    [Fact]
    public async Task TheBrandingSetsTheTitleFaviconAccentColorAndLogo()
    {
        await Start(new FakeHandler("""
            { "apiBaseUrl": "https://cms.example.com",
              "branding": { "name": "Acme", "logoUrl": "img/logo.svg", "faviconUrl": "favicon.png", "accentColor": "#0f6cbd" } }
            """));
        var head = Render<HeadOutlet>();

        Render<AdminApp>();

        head.Find("title").TextContent.ShouldBe("Acme");
        head.Find("link[rel='icon']").GetAttribute("href").ShouldBe("favicon.png");
        var calls = JSInterop.Invocations[BrandingHead.SetPropertyFunction];
        calls.Select(call => call.Arguments.ToArray()).ShouldBe(
        [
            [BrandingHead.AccentProperty, "#0f6cbd"],
            [BrandingHead.LogoProperty, "url(\"http://localhost/img/logo.svg\")"],
        ]);
    }

    [Fact]
    public async Task WithoutBrandingNoCustomPropertyIsSet()
    {
        await Start(new FakeHandler("""{ "apiBaseUrl": "https://cms.example.com" }"""));
        var head = Render<HeadOutlet>();

        Render<AdminApp>();

        head.Find("title").TextContent.ShouldBe("FluentCMS");
        head.FindAll("link").ShouldBeEmpty();
        JSInterop.Invocations[BrandingHead.SetPropertyFunction].ShouldBeEmpty();
    }

    private async Task Start(FakeHandler handler)
    {
        using var http = handler.CreateClient();
        var startup = await AdminConfigurationLoader.LoadAsync(http, Xunit.TestContext.Current.CancellationToken);
        Services.AddAdminStartup(startup);
    }

    private sealed class FakeLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Member)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = (IReadOnlyList<KeyValuePair<string, object?>>)state!;
            Entries.Add((logLevel, (string)values.Single(value => value.Key == "Member").Value!));
        }
    }
}
