using System.Net;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Samples.Blog;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Delivery;

// The protections of UseFluentCms on the real Delivery routes, not on probe endpoints.
public sealed class DeliveryProtectionTests : IAsyncDisposable
{
    private const string Site = "https://site.example.com";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly FluentCmsTestHost _host = new FluentCmsTestHost()
        .WithContentTypes([.. BlogSample.ContentTypes])
        .WithServices(services =>
        {
            TestScheme.Add(services);
            services.PostConfigure<RateLimitOptions>(limits =>
            {
                limits.Delivery.PermitLimit = 2;
                limits.Delivery.MaxKeyLimit = 2;
            });
            services.PostConfigure<SecurityOptions>(security => security.Cors.Delivery.AllowedOrigins.Add(Site));
        });

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task TheListRouteIsRateLimitedAndRefusalsAreProblems()
    {
        var client = _host.CreateClient();

        for (var used = 1; used <= 2; used++)
        {
            using var allowed = await client.GetAsync(new Uri("/api/delivery/v1/article", UriKind.Relative), Token);

            allowed.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
            allowed.Headers.GetValues("RateLimit-Limit").ShouldBe(["2"]);
            allowed.Headers.GetValues("RateLimit-Remaining").ShouldBe([(2 - used).ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        }

        using var refused = await client.GetAsync(new Uri("/api/delivery/v1/article", UriKind.Relative), Token);

        refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        refused.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        refused.Headers.GetValues("Retry-After").Single().ShouldNotBeNullOrWhiteSpace();
        (await refused.Content.ReadAsStringAsync(Token)).ShouldContain("FCMS3110");
    }

    [Fact]
    public async Task AKeyPrincipalHasALimitOfItsOwnOnTheItemRoutes()
    {
        var client = _host.CreateClient();
        (string, string)[] key = [(TestScheme.KindHeader, "ApiKey")];

        for (var request = 0; request < 2; request++)
        {
            using var allowed = await Get(client, "/api/delivery/v1/article/by/slug/missing", key);
            allowed.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
        }

        using var refused = await Get(client, "/api/delivery/v1/article/by/slug/missing", key);
        using var anonymous = await Get(client, "/api/delivery/v1/article");

        refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        anonymous.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task AnAllowedOriginGetsCorsHeadersOnTheRealRoutesAndAnotherGetsNone()
    {
        var client = _host.CreateClient();

        using var allowed = await Get(client, "/api/delivery/v1/article", ("Origin", Site));
        using var other = await Get(client, "/api/delivery/v1/article/by/slug/missing", ("Origin", "https://evil.example.com"));
        using var preflight = await Send(client, HttpMethod.Options, "/api/delivery/v1/article", ("Origin", Site), ("Access-Control-Request-Method", "GET"));

        allowed.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe([Site]);
        other.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
        preflight.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        preflight.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe([Site]);
    }

    [Fact]
    public async Task TheSecurityHeadersAreOnSuccessAndErrorResponsesOfTheRealRoutes()
    {
        var client = _host.CreateClient();

        using var list = await Get(client, "/api/delivery/v1/article");
        using var missing = await Get(client, "/api/delivery/v1/article/by/slug/missing");

        foreach (var response in new[] { list, missing })
        {
            response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
            response.Headers.GetValues("Referrer-Policy").ShouldBe(["no-referrer"]);
            response.Headers.Contains("Content-Security-Policy").ShouldBeFalse();
        }
    }

    private static Task<HttpResponseMessage> Get(HttpClient client, string path, params (string Name, string Value)[] headers) =>
        Send(client, HttpMethod.Get, path, headers);

    private static Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, params (string Name, string Value)[] headers)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        foreach (var (name, value) in headers)
        {
            request.Headers.Add(name, value);
        }

        return client.SendAsync(request, Token);
    }
}
