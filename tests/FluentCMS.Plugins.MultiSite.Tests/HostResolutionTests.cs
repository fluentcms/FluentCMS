using System.Net;
using System.Text.Json.Nodes;
using FluentCMS.Core.Sites;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.MultiSite.Tests.MultiSiteTesting;

namespace FluentCMS.Plugins.MultiSite.Tests;

// The site of a request by its host name (spec 4.6): after an API key's binding, the X-Cms-Site header and the site
// parameter, and before the default site. The default site and a second site each have a host name and one article.
public sealed class HostResolutionTests(HostResolutionTests.TwoSites fixture) : IClassFixture<HostResolutionTests.TwoSites>
{
    private const string OfFirst = "Of the first site";
    private const string OfSecond = "Of the second site";

    [Fact]
    public async Task ADeliveryReadOnAHostNameReturnsTheEntriesOfItsSiteFromTheOutputCacheToo()
    {
        using var client = fixture.Host.CreateClient();

        var (second, secondResponse) = await ArticlesAsync(client, fixture.SecondHost);
        var (first, firstResponse) = await ArticlesAsync(client, fixture.FirstHost);
        var (secondAgain, secondCached) = await ArticlesAsync(client, fixture.SecondHost);
        var (firstAgain, firstCached) = await ArticlesAsync(client, fixture.FirstHost);

        second.ShouldBe([OfSecond]);
        first.ShouldBe([OfFirst]);
        secondAgain.ShouldBe([OfSecond]);
        firstAgain.ShouldBe([OfFirst]);
        secondResponse.Headers.Age.ShouldBeNull();
        firstResponse.Headers.Age.ShouldBeNull();
        secondCached.Headers.Age.ShouldNotBeNull();
        firstCached.Headers.Age.ShouldNotBeNull();
    }

    // The copy of a read is kept under the site its host name resolved to, so the instance that moved the host name
    // answers with the other site at once, before any eviction has run.
    [Fact]
    public async Task AHostNameThatMovesToAnotherSiteReadsNoCopyOfTheSiteItLeft()
    {
        using var admin = await AdminAsync(fixture.Host);
        using var client = fixture.Host.CreateClient();
        var (left, name) = await SiteWithHostAsync(fixture.Host, admin);
        var joined = await CreateSiteAsync(admin);
        await LetThePublicReadAsync(fixture.Host, joined);
        await PublishArticleAsync(fixture.Host, left, "Of the site it left");
        await PublishArticleAsync(fixture.Host, joined, "Of the site it joined");
        (await ArticlesAsync(client, name)).Titles.ShouldBe(["Of the site it left"]);
        (await ArticlesAsync(client, name)).Response.Headers.Age.ShouldNotBeNull();

        await SendAsync(admin, HttpMethod.Delete, $"{SitesRoute}/{left}/hosts/{name}", expected: HttpStatusCode.NoContent);
        await AddHostAsync(admin, joined, name);

        (await ArticlesAsync(client, name)).Titles.ShouldBe(["Of the site it joined"]);
    }

    [Fact]
    public async Task AnUnknownHostNameReadsTheDefaultSite()
    {
        using var client = fixture.Host.CreateClient();

        (await ArticlesAsync(client, HostName("unknown"), "?sort=title")).Titles.ShouldBe([OfFirst]);
        (await ArticlesAsync(client, "192.0.2.10", "?sort=title")).Titles.ShouldBe([OfFirst]);
        (await ArticlesAsync(client, "localhost", "?sort=title")).Titles.ShouldBe([OfFirst]);
    }

    [Fact]
    public async Task TheHostOfARequestIsMatchedInAnyCaseAndWithItsRootDot()
    {
        using var client = fixture.Host.CreateClient();

        (await ArticlesAsync(client, fixture.SecondHost.ToUpperInvariant(), "?page=1")).Titles.ShouldBe([OfSecond]);
        (await ArticlesAsync(client, fixture.SecondHost + ".", "?page=1")).Titles.ShouldBe([OfSecond]);
        (await ArticlesAsync(client, fixture.SecondHost + ":8080", "?page=1")).Titles.ShouldBe([OfSecond]);
    }

    [Fact]
    public async Task TheHeaderAndTheParameterEachWinOverTheHostName()
    {
        using var client = fixture.Host.CreateClient();
        using var byHeader = new HttpRequestMessage(HttpMethod.Get, new Uri($"http://{fixture.SecondHost}/api/delivery/v1/article"));
        byHeader.Headers.Add("X-Cms-Site", DefaultSite.Id.ToString());

        using var header = await client.SendAsync(byHeader, Token);
        var (parameter, _) = await ArticlesAsync(client, fixture.SecondHost, $"?site={DefaultSite.Id}");
        var (reverse, _) = await ArticlesAsync(client, fixture.FirstHost, $"?site={fixture.Second}");

        (await TitlesAsync(header)).ShouldBe([OfFirst]);
        parameter.ShouldBe([OfFirst]);
        reverse.ShouldBe([OfSecond]);
    }

    // A key bound to one site reads it on any host name. A key bound to both reads the site of the host name.
    [Fact]
    public async Task AnApiKeyBindingWinsOverTheHostName()
    {
        using var admin = await AdminAsync(fixture.Host);
        using var bound = await RoleKeyAsync(fixture.Host, admin, "delivery", null, [DefaultSite.Id], "content.article.read");
        using var both = await RoleKeyAsync(fixture.Host, admin, "delivery", null, [DefaultSite.Id, fixture.Second], "content.article.read");

        (await ArticlesAsync(bound, fixture.SecondHost)).Titles.ShouldBe([OfFirst]);
        (await ArticlesAsync(bound, fixture.FirstHost)).Titles.ShouldBe([OfFirst]);
        (await ArticlesAsync(both, fixture.SecondHost)).Titles.ShouldBe([OfSecond]);
        (await ArticlesAsync(both, fixture.FirstHost)).Titles.ShouldBe([OfFirst]);
    }

    // The management of any site from one host name: an administrator names the site, which wins over the host.
    [Fact]
    public async Task AnAdministratorOnOneHostNameManagesAnotherSiteByNamingIt()
    {
        using var admin = await AdminAsync(fixture.Host);

        var onHost = await SendAsync(admin, HttpMethod.Get, $"http://{fixture.SecondHost}/api/management/v1/content/article");
        var named = await SendAsync(admin, HttpMethod.Get, $"http://{fixture.SecondHost}/api/management/v1/content/article?site={DefaultSite.Id}");

        onHost!["items"]!.AsArray().Select(item => item!["title"]!.GetValue<string>()).ShouldBe([OfSecond]);
        named!["items"]!.AsArray().Select(item => item!["title"]!.GetValue<string>()).ShouldBe([OfFirst]);
    }

    [Fact]
    public async Task AGraphQLQueryOnAHostNameRunsInItsSite()
    {
        using var client = fixture.Host.CreateClient();
        var query = new { query = "{ articleList { items { title } } }" };

        var second = await SendAsync(client, HttpMethod.Post, $"http://{fixture.SecondHost}/graphql", query);
        var first = await SendAsync(client, HttpMethod.Post, $"http://{fixture.FirstHost}/graphql", query);

        Titles(second).ShouldBe([OfSecond]);
        Titles(first).ShouldBe([OfFirst]);

        static List<string> Titles(JsonNode? answer)
        {
            answer!["errors"].ShouldBeNull(answer.ToJsonString());
            return [.. answer["data"]!["articleList"]!["items"]!.AsArray().Select(item => item!["title"]!.GetValue<string>())];
        }
    }

    [Fact]
    public async Task AnMcpCallOnAHostNameRunsInItsSite()
    {
        using var http = await AdminAsync(fixture.Host);

        (await QueryAsync(http, fixture.SecondHost)).ShouldBe([OfSecond]);
        (await QueryAsync(http, fixture.FirstHost)).ShouldBe([OfFirst]);

        static async Task<List<string?>> QueryAsync(HttpClient http, string hostname)
        {
            var transport = new HttpClientTransport(
                new HttpClientTransportOptions { Endpoint = new Uri($"http://{hostname}/mcp"), TransportMode = HttpTransportMode.StreamableHttp },
                http,
                loggerFactory: null,
                ownsHttpClient: false);
            await using var client = await McpClient.CreateAsync(transport, new McpClientOptions { ClientInfo = new Implementation { Name = "fluentcms-tests", Version = "1.0.0" } }, cancellationToken: Token);

            var result = await client.CallToolAsync("query", new Dictionary<string, object?> { ["type"] = "article" }, cancellationToken: Token);
            result.IsError.ShouldNotBe(true);
            return [.. result.StructuredContent.ShouldNotBeNull().GetProperty("items").EnumerateArray().Select(item => item.GetProperty("title").GetString())];
        }
    }

    private static async Task<List<string>> TitlesAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        return [.. JsonNode.Parse(text)!["items"]!.AsArray().Select(item => item!["title"]!.GetValue<string>())];
    }

    public sealed class TwoSites : MultiSiteHost, IAsyncLifetime
    {
        public Guid Second { get; private set; }

        public string FirstHost { get; } = HostName("first");

        public string SecondHost { get; private set; } = "";

        public async ValueTask InitializeAsync()
        {
            using var admin = await AdminAsync(Host);
            await AddHostAsync(admin, DefaultSite.Id, FirstHost);
            await LetThePublicReadAsync(Host, DefaultSite.Id);
            (Second, SecondHost) = await SiteWithHostAsync(Host, admin);

            await PublishArticleAsync(Host, DefaultSite.Id, OfFirst);
            await PublishArticleAsync(Host, Second, OfSecond);
            await DrainOutboxAsync(Host);
        }
    }
}

// Behind a reverse proxy the host name is the one the proxy forwards, when the reverse proxy options say to take it.
public sealed class ForwardedHostTests(ForwardedHostTests.BehindAProxy fixture) : IClassFixture<ForwardedHostTests.BehindAProxy>
{
    [Fact]
    public async Task TheForwardedHostDecidesTheSite()
    {
        using var admin = await AdminAsync(fixture.Host);
        var (site, name) = await SiteWithHostAsync(fixture.Host, admin);
        await PublishArticleAsync(fixture.Host, site, "Behind the proxy");
        using var client = fixture.Host.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("http://proxy.internal/api/delivery/v1/article"));
        request.Headers.Add("X-Forwarded-Host", name);

        using var response = await client.SendAsync(request, Token);
        var text = await response.Content.ReadAsStringAsync(Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        JsonNode.Parse(text)!["items"]!.AsArray().Select(item => item!["title"]!.GetValue<string>()).ShouldBe(["Behind the proxy"]);
    }

    public sealed class BehindAProxy() : MultiSiteHost(null, cms =>
    {
        cms.ReverseProxy.ForwardedHeaders.Enabled = true;
        cms.ReverseProxy.ForwardedHeaders.ForwardHost = true;
    });
}
