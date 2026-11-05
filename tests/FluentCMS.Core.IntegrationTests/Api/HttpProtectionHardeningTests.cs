using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.ApiKeys;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.IntegrationTests.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;
using static FluentCMS.Core.IntegrationTests.Api.HttpProtectionTests;

namespace FluentCMS.Core.IntegrationTests.Api;

// The protections that depend on how the host is set up: which authentication schemes it has, what limits it already set,
// and whether it called UseFluentCms at all.
[Xunit.Collection(ProcessEnvironment.Collection)]
public sealed class HttpProtectionHardeningTests : IAsyncDisposable
{
    private const string DeliveryPath = "/api/delivery/v1/probe";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly CmsWebHost _host = new();

    private WebApplication? _app;

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task ABearerKeyClaimIsPartitionedByTheFluentCmsSchemeWhateverTheDefaultSchemeIs()
    {
        var client = await Start(Limits(delivery: 2), WithPolicyScheme);
        (string, string)[] limited = [("X-Test-Sub", "key-limited"), ("X-Test-Kind", "ApiKey"), ("X-Test-Limit", "4")];

        using var first = await Send(client, DeliveryPath, limited);
        Header(first, "RateLimit-Limit").ShouldBe("4");
        for (var request = 1; request < 4; request++)
        {
            (await Status(client, DeliveryPath, limited)).ShouldBe(HttpStatusCode.OK);
        }

        (await Status(client, DeliveryPath, limited)).ShouldBe(HttpStatusCode.TooManyRequests);
        (await Status(client, DeliveryPath, ("X-Test-Sub", "key-other"), ("X-Test-Kind", "ApiKey"))).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ARequestWithoutAKeyUsesTheDefaultSchemeAndIsPartitionedByAddress()
    {
        var client = await Start(Limits(delivery: 1), WithPolicyScheme);

        (await Status(client, DeliveryPath, ("X-Test-Ip", "10.0.0.1"))).ShouldBe(HttpStatusCode.OK);
        (await Status(client, DeliveryPath, ("X-Test-Ip", "10.0.0.1"))).ShouldBe(HttpStatusCode.TooManyRequests);
        (await Status(client, DeliveryPath, ("X-Test-Ip", "10.0.0.2"))).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ManagementPartitionsByThePrincipalOfTheFluentCmsScheme()
    {
        var client = await Start(Limits(management: 1), WithPolicyScheme);

        (await Status(client, "/api/management/v1/probe", ("X-Test-Sub", "user-a"))).ShouldBe(HttpStatusCode.OK);
        (await Status(client, "/api/management/v1/probe", ("X-Test-Sub", "user-a"))).ShouldBe(HttpStatusCode.TooManyRequests);
        (await Status(client, "/api/management/v1/probe", ("X-Test-Sub", "user-b"))).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ADefaultSchemeThatIsNotTheKeySchemeIsNotMistakenForAKey()
    {
        var client = await Start(Limits(delivery: 3), WithPolicyScheme);

        using var response = await Send(client, DeliveryPath, ("X-Test-Limit", "50"));

        Header(response, "RateLimit-Limit").ShouldBe("3");
    }

    [Fact]
    public async Task AKeyLimitIsClampedToTheConfiguredMaximum()
    {
        var settings = Limits(delivery: 2);
        settings["FluentCms:RateLimits:Delivery:MaxKeyLimit"] = "5";
        var client = await Start(settings);

        using var huge = await Send(client, DeliveryPath, ("X-Test-Sub", "key-a"), ("X-Test-Kind", "ApiKey"), ("X-Test-Limit", int.MaxValue.ToString(CultureInfo.InvariantCulture)));
        using var lower = await Send(client, DeliveryPath, ("X-Test-Sub", "key-b"), ("X-Test-Kind", "ApiKey"), ("X-Test-Limit", "1"));

        Header(huge, "RateLimit-Limit").ShouldBe("5");
        Header(lower, "RateLimit-Limit").ShouldBe("1");
    }

    [Fact]
    public async Task TheDefaultMaximumOfAKeyLimitIsFiniteAndNotBelowTheDefaultLimit()
    {
        await Start();

        var delivery = _app!.Services.GetRequiredService<RateLimitOptions>().Delivery;

        delivery.MaxKeyLimit.ShouldBeGreaterThanOrEqualTo(delivery.PermitLimit);
        delivery.MaxKeyLimit.ShouldBeLessThan(int.MaxValue);
    }

    [Fact]
    public async Task AMaximumBelowTheDefaultLimitStopsTheHost()
    {
        var settings = Limits(delivery: 10);
        settings["FluentCms:RateLimits:Delivery:MaxKeyLimit"] = "5";

        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => Start(settings));

        exception.Code.ShouldBe("FCMS1101");
        exception.Message.ShouldContain("FluentCms:RateLimits:Delivery:MaxKeyLimit");
    }

    [Fact]
    public async Task AddressesOfOneIpv6PrefixShareALimitAndAnIpv4MappedAddressSharesWithItsIpv4Address()
    {
        var client = await Start(Limits(delivery: 1));

        (await Status(client, DeliveryPath, ("X-Test-Ip", "2001:db8:1:2::1"))).ShouldBe(HttpStatusCode.OK);
        (await Status(client, DeliveryPath, ("X-Test-Ip", "2001:db8:1:2:aaaa::9"))).ShouldBe(HttpStatusCode.TooManyRequests);
        (await Status(client, DeliveryPath, ("X-Test-Ip", "2001:db8:1:3::1"))).ShouldBe(HttpStatusCode.OK);

        (await Status(client, DeliveryPath, ("X-Test-Ip", "10.0.0.7"))).ShouldBe(HttpStatusCode.OK);
        (await Status(client, DeliveryPath, ("X-Test-Ip", "::ffff:10.0.0.7"))).ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task AFloodFromOneAddressIsRefusedBeforeAuthenticationRuns()
    {
        var settings = Coarse(3);
        var client = await Start(settings);
        var calls = _app!.Services.GetRequiredService<AuthCalls>();

        for (var request = 0; request < 3; request++)
        {
            (await Status(client, DeliveryPath, ("X-Test-Ip", "10.0.0.1"), ("X-Test-Sub", "key-a"))).ShouldBe(HttpStatusCode.OK);
        }

        var before = calls.Count;
        using var refused = await Send(client, DeliveryPath, ("X-Test-Ip", "10.0.0.1"), ("X-Test-Sub", "key-a"));

        refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await Problem(refused)).GetProperty("code").GetString().ShouldBe("FCMS3110");
        Seconds(refused, "Retry-After").ShouldBeInRange(1, 60);
        refused.Headers.Contains("RateLimit-Limit").ShouldBeFalse();
        calls.Count.ShouldBe(before);
        (await Status(client, DeliveryPath, ("X-Test-Ip", "10.0.0.2"))).ShouldBe(HttpStatusCode.OK);
    }

    // The authorization server answers a GET of the token endpoint itself, while the request authenticates.
    [Theory]
    [InlineData("/connect/token", HttpStatusCode.BadRequest)]
    [InlineData("/account/login", HttpStatusCode.NotFound)]
    [InlineData("/api/management/v1/unknown", HttpStatusCode.NotFound)]
    public async Task TheCoarseLimitAlsoCoversRequestsThatNoEndpointAnswers(string path, HttpStatusCode answer)
    {
        var settings = Coarse(2);
        settings["FluentCms:RateLimits:Auth:PermitLimit"] = "100";
        var client = await Start(settings);

        (await Status(client, path, ("X-Test-Ip", "10.0.0.1"))).ShouldBe(answer);
        (await Status(client, path, ("X-Test-Ip", "10.0.0.1"))).ShouldBe(answer);
        (await Status(client, path, ("X-Test-Ip", "10.0.0.1"))).ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task TheCoarseLimitLeavesHealthAndTheHostsOwnEndpointsAlone()
    {
        var settings = Coarse(1);
        var client = await Start(settings);

        for (var request = 0; request < 4; request++)
        {
            (await Status(client, "/health/probe")).ShouldBe(HttpStatusCode.OK);
            (await Status(client, "/host")).ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task AChunkedJsonBodyOverTheCapOnATypedParameterIsAProblem()
    {
        var client = await Start(new Dictionary<string, string?> { ["FluentCms:Limits:MaxBodySize"] = "100" });

        using var chunked = await PostJson(client, "{\"text\":\"" + new string('a', 500) + "\"}", chunked: true);
        using var declared = await PostJson(client, "{\"text\":\"" + new string('a', 500) + "\"}", chunked: false);
        using var small = await PostJson(client, "{\"text\":\"abc\"}", chunked: true);

        foreach (var refused in new[] { chunked, declared })
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
            refused.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
            (await Problem(refused)).GetProperty("code").GetString().ShouldBe("FCMS3111");
        }

        small.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await small.Content.ReadAsStringAsync(Token)).ShouldBe("3");
    }

    [Theory]
    [InlineData("1000", 5000, 1000)]
    [InlineData("10000", 5000, 5000)]
    [InlineData("none", 5000, 5000)]
    public async Task TheCapNeverRaisesALimitTheHostOrServerAlreadySet(string current, long cap, long expected)
    {
        var client = await Start(new Dictionary<string, string?> { ["FluentCms:Limits:MaxBodySize"] = cap.ToString(CultureInfo.InvariantCulture) });

        using var response = await Send(client, "/api/management/v1/limit", ("X-Test-Max", current));

        (await response.Content.ReadAsStringAsync(Token)).ShouldBe(expected.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task AnEndpointWithItsOwnSizeLimitKeepsIt()
    {
        var client = await Start(new Dictionary<string, string?> { ["FluentCms:Limits:MaxBodySize"] = "100" });

        using var response = await client.PostAsync("/api/management/v1/upload", new StringContent(new string('a', 500)), Token);
        using var limit = await Send(client, "/api/management/v1/upload-limit", ("X-Test-Max", "20000"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await limit.Content.ReadAsStringAsync(Token)).ShouldBe("10000000");
    }

    [Fact]
    public async Task SecurityHeadersKeepWhatAnEndpointSetItself()
    {
        var client = await Start();

        using var own = await Send(client, "/account/own-headers");
        using var policy = await Send(client, "/account/own-csp");
        using var withAncestors = await Send(client, "/account/own-csp-ancestors");

        own.Headers.GetValues("Referrer-Policy").ShouldBe(["strict-origin"]);
        own.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        policy.Headers.GetValues("Content-Security-Policy").ShouldBe(["default-src 'self'; frame-ancestors 'none'"]);
        withAncestors.Headers.GetValues("Content-Security-Policy").ShouldBe(["default-src 'self'; frame-ancestors 'self'"]);
    }

    [Fact]
    public async Task TheHostedPageConventionGivesAPageMappedElsewhereTheHeadersAndTheAuthLimit()
    {
        var client = await Start(Limits(auth: 2));

        using var first = await Send(client, "/page");
        (await Status(client, "/page")).ShouldBe(HttpStatusCode.OK);
        using var refused = await Send(client, "/page");

        first.Headers.GetValues("Content-Security-Policy").ShouldBe(["frame-ancestors 'none'"]);
        first.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        Header(first, "RateLimit-Limit").ShouldBe("2");
        refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Theory]
    [InlineData("/connect/token", HttpStatusCode.BadRequest)]
    [InlineData("/connect/introspect", HttpStatusCode.BadRequest)]
    [InlineData("/connect/revoke", HttpStatusCode.BadRequest)]
    [InlineData("/account/login", HttpStatusCode.NotFound)]
    public async Task TheStrictAuthLimitCoversUnroutedAuthPathsBeforeAuthentication(string path, HttpStatusCode answer)
    {
        var client = await Start(Limits(auth: 1));
        var calls = _app!.Services.GetRequiredService<AuthCalls>();

        (await Status(client, path, ("X-Test-Ip", "10.0.0.1"))).ShouldBe(answer);
        var before = calls.Count;
        using var refused = await Send(client, path, ("X-Test-Ip", "10.0.0.1"));

        refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await Problem(refused)).GetProperty("code").GetString().ShouldBe("FCMS3110");
        Seconds(refused, "Retry-After").ShouldBeInRange(1, 60);
        calls.Count.ShouldBe(before);
        (await Status(client, path, ("X-Test-Ip", "10.0.0.2"))).ShouldBe(answer);
    }

    [Theory]
    [InlineData("/connect/probe")]
    [InlineData("/account/probe")]
    public async Task ARoutedAuthRequestIsCountedOnceAndStillCarriesTheHeaders(string path)
    {
        var client = await Start(Limits(auth: 3));

        for (var used = 1; used <= 3; used++)
        {
            using var allowed = await Send(client, path, ("X-Test-Ip", "10.0.0.1"));

            allowed.StatusCode.ShouldBe(HttpStatusCode.OK);
            Header(allowed, "RateLimit-Limit").ShouldBe("3");
            Header(allowed, "RateLimit-Remaining").ShouldBe((3 - used).ToString(CultureInfo.InvariantCulture));
        }

        (await Status(client, path, ("X-Test-Ip", "10.0.0.1"))).ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task AKeyLimitAboveTheCoarseLimitStopsTheHost()
    {
        var settings = Limits(delivery: 2);
        settings["FluentCms:RateLimits:Delivery:MaxKeyLimit"] = "10";
        settings["FluentCms:RateLimits:PreAuth:PermitLimit"] = "5";

        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => Start(settings));

        exception.Code.ShouldBe("FCMS1101");
        exception.Message.ShouldContain("FluentCms:RateLimits:Delivery:MaxKeyLimit");
        exception.Message.ShouldContain("PreAuth:PermitLimit");
    }

    [Fact]
    public async Task ARealApiKeyWithARateLimitIsLimitedToItAndOtherKeysUseTheDefault()
    {
        var client = await Start(Limits(delivery: 2), WithPolicyScheme);
        var limited = await CreateKey("limited", rateLimit: 3);
        var plain = await CreateKey("plain", rateLimit: null);

        for (var used = 1; used <= 3; used++)
        {
            using var allowed = await Send(client, DeliveryPath, ("X-Api-Key", limited), ("X-Test-Ip", "10.0.0.1"));

            allowed.StatusCode.ShouldBe(HttpStatusCode.OK);
            Header(allowed, "RateLimit-Limit").ShouldBe("3");
            Header(allowed, "RateLimit-Remaining").ShouldBe((3 - used).ToString(CultureInfo.InvariantCulture));
        }

        using var refused = await Send(client, DeliveryPath, ("X-Api-Key", limited), ("X-Test-Ip", "10.0.0.2"));
        refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await Problem(refused)).GetProperty("code").GetString().ShouldBe("FCMS3110");

        using var other = await Send(client, DeliveryPath, ("X-Api-Key", plain), ("X-Test-Ip", "10.0.0.1"));
        other.StatusCode.ShouldBe(HttpStatusCode.OK);
        Header(other, "RateLimit-Limit").ShouldBe("2");
    }

    [Fact]
    public async Task ARealApiKeyIsPartitionedByItselfAndAuthorizationHeaderKeysWorkToo()
    {
        var client = await Start(Limits(delivery: 1), WithPolicyScheme);
        var first = await CreateKey("first", rateLimit: null);
        var second = await CreateKey("second", rateLimit: null);

        (await Status(client, DeliveryPath, ("Authorization", $"ApiKey {first}"))).ShouldBe(HttpStatusCode.OK);
        (await Status(client, DeliveryPath, ("X-Api-Key", first))).ShouldBe(HttpStatusCode.TooManyRequests);
        (await Status(client, DeliveryPath, ("X-Api-Key", second))).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AKeyThatDoesNotAuthenticateIsLimitedLikeAnAnonymousCaller()
    {
        var client = await Start(Limits(delivery: 1), WithPolicyScheme);

        (await Status(client, DeliveryPath, ("X-Api-Key", "fcms_dk_not-a-key"), ("X-Test-Ip", "10.0.0.1"))).ShouldBe(HttpStatusCode.OK);
        (await Status(client, DeliveryPath, ("X-Api-Key", "fcms_dk_other-wrong"), ("X-Test-Ip", "10.0.0.1"))).ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task MapFluentCmsWithoutUseFluentCmsStopsTheHostAtStartup()
    {
        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => Start(pipeline: _ => { }));

        exception.Code.ShouldBe("FCMS1104");
        exception.Message.ShouldContain("UseFluentCms");
    }

    // A coarse limit, and the limits below it that the options require to be no higher.
    private static Dictionary<string, string?> Coarse(int limit)
    {
        var value = limit.ToString(CultureInfo.InvariantCulture);
        return new Dictionary<string, string?>
        {
            ["FluentCms:RateLimits:PreAuth:PermitLimit"] = value,
            ["FluentCms:RateLimits:Delivery:PermitLimit"] = value,
            ["FluentCms:RateLimits:Delivery:MaxKeyLimit"] = value,
        };
    }

    // A host whose default scheme is a browser one. The FluentCms scheme, which AddFluentCms registers, sends a request with
    // an API key to the key scheme and any other to the bearer scheme, which stands in for the authorization server's.
    private static void WithPolicyScheme(IServiceCollection services)
    {
        services.AddSingleton<AuthCalls>();
        services.AddAuthentication(BrowserAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, BrowserAuthHandler>(BrowserAuthHandler.SchemeName, _ => { })
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        services.Configure<FluentCmsAuthenticationOptions>(FluentCmsAuthentication.Scheme, options => options.BearerScheme = TestAuthHandler.SchemeName);
    }

    private async Task<string> CreateKey(string name, int? rateLimit)
    {
        await using var scope = _app!.Services.CreateAsyncScope();
        var keys = scope.ServiceProvider.GetRequiredService<ApiKeyService>();
        var created = await keys.CreateAsync(new ApiKeyRequest(name, ApiKeyClass.Delivery, BuiltInRoles.Public) { RateLimit = rateLimit }, Token);
        return created.Secret;
    }

    private static Task<HttpResponseMessage> PostJson(HttpClient client, string json, bool chunked)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/management/v1/json")
        {
            Content = chunked ? new StreamContent(new UnseekableStream(bytes)) : new ByteArrayContent(bytes),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return client.SendAsync(request, Token);
    }

    private async Task<HttpClient> Start(
        Dictionary<string, string?>? settings = null,
        Action<IServiceCollection>? authentication = null,
        Action<WebApplication>? pipeline = null)
    {
        var app = _host.Build(
            Environments.Development,
            settings,
            builder =>
            {
                (authentication ?? (services => services.AddTestAuthentication()))(builder.Services);
                foreach (var group in Enum.GetValues<FluentCmsEndpointGroup>())
                {
                    builder.Services.AddFluentCmsEndpoints(group, routes =>
                    {
                        routes.MapGet("/probe", () => "ok");
                        routes.MapPost("/json", (Payload payload) => payload.Text.Length.ToString(CultureInfo.InvariantCulture));
                        routes.MapGet("/limit", (HttpContext context) => context.Features.Get<IHttpMaxRequestBodySizeFeature>()?.MaxRequestBodySize?.ToString(CultureInfo.InvariantCulture));
                        routes.MapGet("/upload-limit", (HttpContext context) => context.Features.Get<IHttpMaxRequestBodySizeFeature>()?.MaxRequestBodySize?.ToString(CultureInfo.InvariantCulture)).WithMetadata(new RequestSizeLimitAttribute(10_000_000));
                        routes.MapPost("/upload", async (HttpRequest request) =>
                        {
                            using var reader = new StreamReader(request.Body);
                            return (await reader.ReadToEndAsync()).Length.ToString(CultureInfo.InvariantCulture);
                        }).WithMetadata(new RequestSizeLimitAttribute(10_000_000));
                        routes.MapGet("/own-headers", (HttpContext context) =>
                        {
                            context.Response.Headers["Referrer-Policy"] = "strict-origin";
                            context.Response.Headers.XContentTypeOptions = "nosniff";
                            return "ok";
                        });
                        routes.MapGet("/own-csp", (HttpContext context) =>
                        {
                            context.Response.Headers.ContentSecurityPolicy = "default-src 'self';";
                            return "ok";
                        });
                        routes.MapGet("/own-csp-ancestors", (HttpContext context) =>
                        {
                            context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; frame-ancestors 'self'";
                            return "ok";
                        });
                    });
                }
            },
            pipeline: pipeline ?? (app =>
            {
                app.Use((context, next) =>
                {
                    if (context.Request.Headers.TryGetValue("X-Test-Ip", out var address))
                    {
                        context.Connection.RemoteIpAddress = IPAddress.Parse(address.ToString());
                    }

                    if (context.Request.Headers.TryGetValue("X-Test-Max", out var max))
                    {
                        context.Features.Set<IHttpMaxRequestBodySizeFeature>(new BodySizeFeature(max == "none" ? null : long.Parse(max.ToString(), CultureInfo.InvariantCulture)));
                    }

                    return next(context);
                });
                app.UseFluentCms();
                app.MapGet("/host", () => "host");
                app.MapGet("/page", () => "page").AsHostedPages();
            }));

        _app = app;
        await app.StartAsync(Token);
        return app.GetTestClient();
    }

    private sealed record Payload(string Text);

    private sealed class BodySizeFeature(long? limit) : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => false;

        public long? MaxRequestBodySize { get; set; } = limit;
    }

    private sealed class UnseekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;

        public override long Length => throw new NotSupportedException();
    }
}
