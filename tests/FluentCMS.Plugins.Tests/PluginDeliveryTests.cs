using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Events;
using FluentCMS.Core.Identity.ApiKeys;
using FluentCMS.Core.Plugins;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using KeyClass = FluentCMS.Core.Identity.ApiKeys.ApiKeyClass;

namespace FluentCMS.Plugins.Tests;

// What a plugin's MapDelivery group gets from core (#2888): what core's Delivery group has, through the sample plugin's
// /api/delivery/v1/sample routes.
public sealed class PluginDeliveryTests(PluginDeliveryFixture fixture) : IClassFixture<PluginDeliveryFixture>
{
    private const string AllowedOrigin = "https://site.example.com";
    private const string Greeting = "/api/delivery/v1/sample/greeting";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheGroupIsServedUnderTheDeliveryApiAndThePathBaseToAnAnonymousCaller()
    {
        await using var host = new FluentCmsTestHost().WithServices(services => services.PostConfigure<ReverseProxyOptions>(proxy => proxy.PathBase = "/cms"));
        using var client = host.CreateClient();

        using var served = await client.GetAsync(new Uri("/cms" + Greeting, UriKind.Relative), Token);
        using var outside = await client.GetAsync(new Uri(Greeting, UriKind.Relative), Token);

        served.StatusCode.ShouldBe(HttpStatusCode.OK, await served.Content.ReadAsStringAsync(Token));
        var body = await JsonAsync(served);
        body["text"]!.GetValue<string>().ShouldBe("discovered");
        body["site"]!.GetValue<Guid>().ShouldBe(DefaultSite.Id);
        body["locale"]!.GetValue<string>().ShouldBe("en");
        outside.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TheGroupRunsInTheSiteAndLocaleTheRequestNames()
    {
        var host = fixture.Host;
        var docs = await CreateSiteAsync(host, "fr");
        using var client = host.CreateClient();

        var named = await JsonAsync(await SendAsync(client, Greeting, ("X-Cms-Site", docs.Slug)));
        using var locale = await SendAsync(client, Greeting + $"?site={docs.Slug}&locale=de");
        using var missing = await SendAsync(client, Greeting + "?site=nowhere");

        named["site"]!.GetValue<Guid>().ShouldBe(docs.Id);
        named["locale"]!.GetValue<string>().ShouldBe("fr");
        locale.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(locale)).ShouldBe("FCMS2312");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(missing)).ShouldBe("FCMS2301");
    }

    // A credential that is sent and refused is never read as anonymous, as on core's Delivery routes. The copy is kept in a
    // site of the test's own, so that no other test is answered from it.
    [Fact]
    public async Task TheDeliveryOutputCacheKeepsTheAnswerUnlessTheEndpointOptsOutAndARefusedKeyIsA401()
    {
        var host = fixture.Host;
        var site = ("X-Cms-Site", (await CreateSiteAsync(host, "en")).Slug);
        using var client = host.CreateClient();
        using var wrongKey = host.CreateClient();
        wrongKey.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", ApiKeyFormat.Generate(KeyClass.Delivery));

        var first = await ServedAsync(client, Greeting, site);
        var second = await ServedAsync(client, Greeting, site);
        var fresh = await ServedAsync(client, "/api/delivery/v1/sample/fresh");
        var again = await ServedAsync(client, "/api/delivery/v1/sample/fresh");
        using var refused = await SendAsync(wrongKey, Greeting, site);

        second.ShouldBe(first);
        again.ShouldBe(fresh + 1);
        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodeAsync(refused)).ShouldBe("FCMS3016");
    }

    [Fact]
    public async Task TheGroupHasTheDeliveryRateLimitAndCorsPolicy()
    {
        await using var host = new FluentCmsTestHost().WithServices(services =>
        {
            services.PostConfigure<RateLimitOptions>(limits =>
            {
                limits.Delivery.PermitLimit = 2;
                limits.Delivery.MaxKeyLimit = 2;
            });
            services.PostConfigure<SecurityOptions>(security => security.Cors.Delivery.AllowedOrigins.Add(AllowedOrigin));
        });
        using var client = host.CreateClient();

        using var allowed = await SendAsync(client, Greeting, ("Origin", AllowedOrigin));
        using var other = await SendAsync(client, Greeting, ("Origin", "https://elsewhere.example.com"));
        using var limited = await SendAsync(client, Greeting);

        allowed.StatusCode.ShouldBe(HttpStatusCode.OK);
        allowed.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe([AllowedOrigin]);
        allowed.Headers.GetValues("RateLimit-Limit").ShouldBe(["2"]);
        other.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await CodeAsync(limited)).ShouldBe("FCMS3110");
    }

    // A copy the output cache kept is served before the per-site gate runs, so disabling the plugin lets go of its site.
    [Fact]
    public async Task ACopyOfTheAnswerGoesWhenThePluginIsDisabledInItsSite()
    {
        var host = fixture.Host;
        var docs = await CreateSiteAsync(host, "en");
        using var client = host.CreateClient();
        var kept = await ServedAsync(client, Greeting, ("X-Cms-Site", docs.Slug));
        (await ServedAsync(client, Greeting, ("X-Cms-Site", docs.Slug))).ShouldBe(kept);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            using var system = SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), Origin.Seed, new SiteId(DefaultSite.Id));
            await scope.ServiceProvider.GetRequiredService<PluginAdministration>().SetEnabledAsync("sample", docs.Id, false, Token);
        }

        // The eviction runs through the outbox, which no worker drains, so the copy is gone once it is drained.
        while (await host.Services.GetRequiredService<OutboxDrainer>().DrainAsync(Token) > 0)
        {
        }

        using var refused = await SendAsync(client, Greeting, ("X-Cms-Site", docs.Slug));
        using var served = await SendAsync(client, Greeting);

        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(refused)).ShouldBe("FCMS1550");
        served.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/{area}")]
    [InlineData("/*rest")]
    [InlineData("/note")]
    [InlineData("/Note/files")]
    [InlineData("routed")]
    [InlineData("/routed/")]
    public void APrefixThatWouldTakeCoreDeliveryRoutesStopsTheHost(string prefix)
    {
        using var host = new FluentCmsTestHost().WithContentTypes<Note>().Configure(cms => cms.AddPlugin<RoutedPlugin>(routed => (routed.Prefix, routed.Delivery) = (prefix, true)));

        var exception = Should.Throw<FluentCmsConfigurationException>(() => host.Services);

        exception.Code.ShouldBe("FCMS1524");
        exception.Message.ShouldContain("'routed'");
        exception.Message.ShouldContain("MapDelivery");
    }

    [Fact]
    public void ARawPrefixUnderTheDeliveryApiStillStopsTheHostAndNamesMapDelivery()
    {
        using var host = new FluentCmsTestHost().Configure(cms => cms.AddPlugin<RoutedPlugin>(routed => routed.Prefix = "/api/delivery/v1/routed"));

        var exception = Should.Throw<FluentCmsConfigurationException>(() => host.Services);

        exception.Code.ShouldBe("FCMS1524");
        exception.Message.ShouldContain("MapDelivery");
    }

    // A site of the test's own, since the tests share a host.
    private static async Task<Site> CreateSiteAsync(FluentCmsTestHost host, string locale)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SiteManager>().CreateAsync("Docs", $"docs-{Guid.NewGuid():N}", locale, Token);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string path, params (string Name, string Value)[] headers)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        foreach (var (name, value) in headers)
        {
            request.Headers.Add(name, value);
        }

        return await client.SendAsync(request, Token);
    }

    private static async Task<int> ServedAsync(HttpClient client, string path, params (string Name, string Value)[] headers)
    {
        using var response = await SendAsync(client, path, headers);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await JsonAsync(response))["served"]!.GetValue<int>();
    }

    private static async Task<JsonNode> JsonAsync(HttpResponseMessage response) => JsonNode.Parse(await response.Content.ReadAsStringAsync(Token))!;

    private static async Task<string> CodeAsync(HttpResponseMessage response) => (await JsonAsync(response))["code"]!.GetValue<string>();
}
