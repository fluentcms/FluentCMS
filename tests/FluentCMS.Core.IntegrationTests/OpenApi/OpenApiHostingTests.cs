using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity;
using FluentCMS.Core.IntegrationTests.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.OpenApi;

// Where MapFluentCmsOpenApi puts the document and the UI, and when it leaves them out.
public sealed partial class OpenApiHostingTests : IAsyncDisposable
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly CmsWebHost _host = new();

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Theory]
    [InlineData("Production", null, false)]
    [InlineData("Staging", null, false)]
    [InlineData("Development", null, true)]
    [InlineData("Production", "true", true)]
    [InlineData("Development", "false", false)]
    public async Task TheyAreOnInDevelopmentAndElsewhereOnlyWhenEnabled(string environment, string? enabled, bool served)
    {
        using var client = await StartAsync(environment, new() { ["FluentCms:OpenApi:Enabled"] = enabled }, app => app.MapFluentCmsOpenApi());

        using var document = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), Token);
        using var ui = await client.GetAsync(new Uri("/scalar/", UriKind.Relative), Token);

        document.StatusCode.ShouldBe(served ? HttpStatusCode.OK : HttpStatusCode.NotFound);
        ui.StatusCode.ShouldBe(served ? HttpStatusCode.OK : HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UnderThePathBaseTheServerAndTheAuthorizationServerCarryIt()
    {
        using var client = await StartAsync(Environments.Development, new() { ["FluentCms:ReverseProxy:PathBase"] = "/cms" }, app => app.MapFluentCmsOpenApi());

        var document = JsonNode.Parse(await client.GetStringAsync(new Uri("/cms/openapi/v1.json", UriKind.Relative), Token))!;
        var page = await client.GetStringAsync(new Uri("/cms/scalar/", UriKind.Relative), Token);
        using var unprefixed = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), Token);

        unprefixed.StatusCode.ShouldBe(HttpStatusCode.OK);
        document["servers"]![0]!["url"]!.GetValue<string>().TrimEnd('/').ShouldBe("http://localhost/cms");
        document["components"]!["securitySchemes"]!["oauth2"]!["flows"]!["authorizationCode"]!["authorizationUrl"]!.GetValue<string>()
            .ShouldBe("http://localhost/cms/connect/authorize");
        page.ShouldContain("\"url\":\"openapi/v1.json\"");
    }

    // The document is asked for on another host, as a forged Host header would name it.
    [Theory]
    [InlineData("https://cms.example.com", null, "https://cms.example.com")]
    [InlineData("https://cms.example.com", "/cms", "https://cms.example.com/cms")]
    [InlineData("https://cms.example.com/cms/", "/cms", "https://cms.example.com/cms")]
    [InlineData(null, "/cms", "http://elsewhere.example/cms")]
    public async Task WithAPublicOriginTheServerAndTheAuthorizationServerAreAtIt(string? origin, string? pathBase, string expected)
    {
        using var client = await StartAsync(Environments.Development, new() { ["FluentCms:PublicOrigin"] = origin, ["FluentCms:ReverseProxy:PathBase"] = pathBase }, app => app.MapFluentCmsOpenApi());
        client.BaseAddress = new Uri("http://elsewhere.example/");

        var document = JsonNode.Parse(await client.GetStringAsync(new Uri($"{pathBase}/openapi/v1.json", UriKind.Relative), Token))!;
        var flows = document["components"]!["securitySchemes"]!["oauth2"]!["flows"]!;

        document["servers"]!.AsArray().ShouldHaveSingleItem()!["url"]!.GetValue<string>().TrimEnd('/').ShouldBe(expected);
        flows["authorizationCode"]!["authorizationUrl"]!.GetValue<string>().ShouldBe($"{expected}/connect/authorize");
        flows["authorizationCode"]!["tokenUrl"]!.GetValue<string>().ShouldBe($"{expected}/connect/token");
        flows["authorizationCode"]!["refreshUrl"]!.GetValue<string>().ShouldBe($"{expected}/connect/token");
        flows["clientCredentials"]!["tokenUrl"]!.GetValue<string>().ShouldBe($"{expected}/connect/token");
    }

    [Fact]
    public async Task InAGroupTheUiFindsTheDocumentBesideIt()
    {
        using var client = await StartAsync(Environments.Development, [], app => app.MapGroup("/docs").MapFluentCmsOpenApi());

        using var document = await client.GetAsync(new Uri("/docs/openapi/v1.json", UriKind.Relative), Token);
        var page = await client.GetStringAsync(new Uri("/docs/scalar/", UriKind.Relative), Token);
        using var root = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), Token);

        document.StatusCode.ShouldBe(HttpStatusCode.OK);
        page.ShouldContain("\"url\":\"docs/openapi/v1.json\"");
        root.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TheReturnedBuilderAppliesConventionsToBoth()
    {
        var marker = new object();
        var app = _host.Build(Environments.Development, pipeline: app =>
        {
            app.UseFluentCms();
            app.MapFluentCmsOpenApi().WithMetadata(marker);
        });
        await app.StartAsync(Token);

        var marked = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.Contains(marker))
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToList();

        marked.ShouldContain("/openapi/v1.json");
        marked.ShouldContain(pattern => pattern!.StartsWith("/scalar", StringComparison.Ordinal));
    }

    // A key sent to them is never looked up, so one that does not exist is no reason to refuse.
    [Fact]
    public async Task TheyCarryTheFluentCmsHeadersAndTheDeliveryLimit()
    {
        using var client = await StartAsync(Environments.Development, new() { ["FluentCms:RateLimits:Delivery:PermitLimit"] = "2" }, app => app.MapFluentCmsOpenApi());

        using var document = await SendAsync(client, "/openapi/v1.json", ("X-Api-Key", "not-a-key"));
        using var ui = await SendAsync(client, "/scalar/");
        using var refused = await SendAsync(client, "/openapi/v1.json");

        foreach (var response in new[] { document, ui })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
            response.Headers.GetValues("Referrer-Policy").ShouldBe(["no-referrer"]);
            response.Headers.GetValues("RateLimit-Limit").ShouldBe(["2"]);
        }

        refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task TheCoarseLimitCountsThemBeforeAuthentication()
    {
        using var client = await StartAsync(Environments.Development, new()
        {
            ["FluentCms:RateLimits:PreAuth:PermitLimit"] = "1",
            ["FluentCms:RateLimits:Delivery:PermitLimit"] = "1",
            ["FluentCms:RateLimits:Delivery:MaxKeyLimit"] = "1",
        }, app => app.MapFluentCmsOpenApi());

        using var first = await SendAsync(client, "/scalar/");
        using var second = await SendAsync(client, "/openapi/v1.json");

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        second.Headers.Contains("RateLimit-Limit").ShouldBeFalse();
    }

    [Fact]
    public async Task TheDocumentIsBuiltOnceAndThenServedFromTheCache()
    {
        var builds = new Builds();
        using var client = await StartAsync(Environments.Development, [], app => app.MapFluentCmsOpenApi(), builds.Count);

        var first = await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), Token);
        var second = await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), Token);

        builds.Value.ShouldBe(1);
        second.ShouldBe(first);
    }

    // Without a public origin the document names the host it was asked on, so each host has a copy of its own.
    [Theory]
    [InlineData(null, 2)]
    [InlineData("https://cms.example.com", 1)]
    public async Task EachHostHasACopyOnlyWhenTheDocumentNamesIt(string? origin, int expected)
    {
        var builds = new Builds();
        using var client = await StartAsync(Environments.Development, new() { ["FluentCms:PublicOrigin"] = origin }, app => app.MapFluentCmsOpenApi(), builds.Count);

        var first = JsonNode.Parse(await client.GetStringAsync(new Uri("http://one.example/openapi/v1.json"), Token))!;
        var second = JsonNode.Parse(await client.GetStringAsync(new Uri("http://two.example/openapi/v1.json"), Token))!;
        _ = await client.GetStringAsync(new Uri("http://two.example/openapi/v1.json"), Token);

        builds.Value.ShouldBe(expected);
        Server(first).ShouldBe(origin ?? "http://one.example");
        Server(second).ShouldBe(origin ?? "http://two.example");
    }

    // UseFluentCms authorizes before the output cache, so the kept copy reaches only a caller the policy lets through.
    [Fact]
    public async Task ADocumentBehindAnAuthorizationPolicyIsKeptAndStillRefusedToWhomThePolicyRefuses()
    {
        var builds = new Builds();
        using var client = await StartAsync(
            Environments.Development,
            [],
            app => app.MapFluentCmsOpenApi().RequireAuthorization("docs"),
            builder =>
            {
                builds.Count(builder);
                builder.Services.AddAuthorizationBuilder().AddPolicy("docs", policy => policy
                    .AddAuthenticationSchemes(FluentCmsAuthentication.Scheme)
                    .RequireAssertion(context => context.Resource is HttpContext http && http.Request.Headers.ContainsKey("X-Docs")));
            });

        using var allowed = await SendAsync(client, "/openapi/v1.json", ("X-Docs", "1"));
        using var refused = await SendAsync(client, "/openapi/v1.json");
        using var again = await SendAsync(client, "/openapi/v1.json", ("X-Docs", "1"));

        allowed.StatusCode.ShouldBe(HttpStatusCode.OK);
        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        again.Headers.Age.ShouldNotBeNull();
        builds.Value.ShouldBe(1);
    }

    // Scalar serves its bundle and helper beside the page from its embedded assets, and starts it with an inline script.
    [Fact]
    public async Task TheUiRunsOnlyItsOwnScripts()
    {
        using var client = await StartAsync(Environments.Development, [], app => app.MapFluentCmsOpenApi());

        using var first = await SendAsync(client, "/scalar/");
        using var second = await SendAsync(client, "/scalar/");
        var page = await first.Content.ReadAsStringAsync(Token);
        var policy = first.Headers.GetValues("Content-Security-Policy").Single();
        var nonce = Nonce().Match(policy).Groups[1].Value;

        nonce.ShouldNotBeEmpty();
        Nonce().Match(second.Headers.GetValues("Content-Security-Policy").Single()).Groups[1].Value.ShouldNotBe(nonce);
        policy.ShouldStartWith("default-src 'none'; ");
        policy.ShouldContain($"script-src 'self' 'nonce-{nonce}';");
        policy.ShouldContain("connect-src 'self';");
        policy.ShouldContain("frame-ancestors 'none'");
        Scripts().Count(page).ShouldBe(3);
        Scripts().Matches(page).ShouldAllBe(script => script.Value.Contains($"nonce=\"{nonce}\"", StringComparison.Ordinal));
        page.ShouldContain("src=\"scalar.js\"");
        page.ShouldNotContain("//");
        first.Headers.CacheControl!.NoStore.ShouldBeTrue();

        using var bundle = await SendAsync(client, "/scalar/scalar.js");
        bundle.StatusCode.ShouldBe(HttpStatusCode.OK);
        bundle.Content.Headers.ContentType!.MediaType.ShouldBe("text/javascript");
    }

    [Fact]
    public async Task TheUiSendsRequestsToThePublicOrigin()
    {
        using var client = await StartAsync(Environments.Development, new() { ["FluentCms:PublicOrigin"] = "https://cms.example.com" }, app => app.MapFluentCmsOpenApi());

        using var ui = await SendAsync(client, "/scalar/");

        ui.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("connect-src 'self' https://cms.example.com;");
    }

    [Fact]
    public void WithoutAddFluentCmsMappingThrows()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        using var app = builder.Build();

        var exception = Should.Throw<FluentCmsConfigurationException>(() => app.MapFluentCmsOpenApi());

        exception.Code.ShouldBe("FCMS1103");
    }

    [GeneratedRegex("'nonce-([^']+)'")]
    private static partial Regex Nonce();

    [GeneratedRegex("<script[^>]*>")]
    private static partial Regex Scripts();

    private static string Server(JsonNode document) => document["servers"]![0]!["url"]!.GetValue<string>().TrimEnd('/');

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string path, params (string Name, string Value)[] headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        foreach (var (name, value) in headers)
        {
            request.Headers.Add(name, value);
        }

        return client.SendAsync(request, Token);
    }

    private async Task<HttpClient> StartAsync(string environment, Dictionary<string, string?> settings, Action<WebApplication> map, Action<WebApplicationBuilder>? configure = null)
    {
        var app = _host.Build(environment, settings, configure, pipeline: app =>
        {
            app.UseFluentCms();
            map(app);
        });
        await app.StartAsync(Token);
        return app.GetTestClient();
    }

    // Counts the documents built, by a transformer that runs once for each.
    private sealed class Builds
    {
        private int _value;

        public int Value => _value;

        public void Count(WebApplicationBuilder builder) =>
            builder.Services.Configure<OpenApiOptions>("v1", options => options.AddDocumentTransformer((_, _, _) =>
            {
                Interlocked.Increment(ref _value);
                return Task.CompletedTask;
            }));
    }
}
