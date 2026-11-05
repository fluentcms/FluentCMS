using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.IntegrationTests.Hosting;
using FluentCMS.Core.IntegrationTests.Testing;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Schema;
using FluentCMS.Sites;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Api;

public sealed class HttpProtectionTests : IAsyncDisposable
{
    private const string DeliveryPath = "/api/delivery/v1/probe";
    private const string ManagementPath = "/api/management/v1/probe";
    private const string ConnectPath = "/connect/probe";
    private const string AccountPath = "/account/probe";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly CmsWebHost _host = new();

    private WebApplication? _app;

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task ExceedingAPolicyIsA429ProblemWithRetryAfterAndRateLimitHeaders()
    {
        var client = await Start(Limits(delivery: 3));

        for (var used = 1; used <= 3; used++)
        {
            using var allowed = await Send(client, DeliveryPath, ("X-Test-Ip", "10.0.0.1"));
            allowed.StatusCode.ShouldBe(HttpStatusCode.OK);
            Header(allowed, "RateLimit-Limit").ShouldBe("3");
            Header(allowed, "RateLimit-Remaining").ShouldBe((3 - used).ToString(CultureInfo.InvariantCulture));
            Seconds(allowed, "RateLimit-Reset").ShouldBeInRange(1, 60);
        }

        using var refused = await Send(client, DeliveryPath, ("X-Test-Ip", "10.0.0.1"));

        refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        refused.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        Seconds(refused, "Retry-After").ShouldBeInRange(1, 60);
        Header(refused, "RateLimit-Limit").ShouldBe("3");
        Header(refused, "RateLimit-Remaining").ShouldBe("0");
        Seconds(refused, "RateLimit-Reset").ShouldBeInRange(1, 60);
        var problem = await Problem(refused);
        problem.GetProperty("code").GetString().ShouldBe("FCMS3110");
        problem.GetProperty("status").GetInt32().ShouldBe(429);
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task DeliveryPartitionsAnonymousCallersByAddress()
    {
        var client = await Start(Limits(delivery: 1));

        (await Status(client, DeliveryPath, ("X-Test-Ip", "10.0.0.1"))).ShouldBe(HttpStatusCode.OK);
        (await Status(client, DeliveryPath, ("X-Test-Ip", "10.0.0.1"))).ShouldBe(HttpStatusCode.TooManyRequests);
        (await Status(client, DeliveryPath, ("X-Test-Ip", "10.0.0.2"))).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeliveryPartitionsAnApiKeyByItsSubjectNotItsAddress()
    {
        var client = await Start(Limits(delivery: 1));

        (await Status(client, DeliveryPath, ("X-Test-Ip", "10.0.0.1"), ("X-Test-Sub", "key-a"), ("X-Test-Kind", "ApiKey"))).ShouldBe(HttpStatusCode.OK);
        (await Status(client, DeliveryPath, ("X-Test-Ip", "10.0.0.2"), ("X-Test-Sub", "key-a"), ("X-Test-Kind", "ApiKey"))).ShouldBe(HttpStatusCode.TooManyRequests);
        (await Status(client, DeliveryPath, ("X-Test-Ip", "10.0.0.1"), ("X-Test-Sub", "key-b"), ("X-Test-Kind", "ApiKey"))).ShouldBe(HttpStatusCode.OK);
        (await Status(client, DeliveryPath, ("X-Test-Ip", "10.0.0.1"))).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AKeysOwnLimitReplacesTheDefault()
    {
        var client = await Start(Limits(delivery: 2));
        (string, string)[] limited = [("X-Test-Sub", "key-limited"), ("X-Test-Kind", "ApiKey"), ("X-Test-Limit", "4")];
        (string, string)[] plain = [("X-Test-Sub", "key-plain"), ("X-Test-Kind", "ApiKey")];

        for (var request = 0; request < 4; request++)
        {
            using var allowed = await Send(client, DeliveryPath, limited);
            allowed.StatusCode.ShouldBe(HttpStatusCode.OK);
            Header(allowed, "RateLimit-Limit").ShouldBe("4");
        }

        (await Status(client, DeliveryPath, limited)).ShouldBe(HttpStatusCode.TooManyRequests);

        (await Status(client, DeliveryPath, plain)).ShouldBe(HttpStatusCode.OK);
        (await Status(client, DeliveryPath, plain)).ShouldBe(HttpStatusCode.OK);
        (await Status(client, DeliveryPath, plain)).ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task AKeyThatIsNotAnApiKeyOrHasNoPositiveLimitUsesTheDefault()
    {
        var client = await Start(Limits(delivery: 2));

        using var user = await Send(client, DeliveryPath, ("X-Test-Sub", "someone"), ("X-Test-Kind", "AdminUser"), ("X-Test-Limit", "50"), ("X-Test-Ip", "10.0.0.5"));
        using var zero = await Send(client, DeliveryPath, ("X-Test-Sub", "key-zero"), ("X-Test-Kind", "ApiKey"), ("X-Test-Limit", "0"));

        Header(user, "RateLimit-Limit").ShouldBe("2");
        Header(zero, "RateLimit-Limit").ShouldBe("2");
    }

    [Fact]
    public async Task ManagementPartitionsByPrincipal()
    {
        var client = await Start(Limits(management: 2));

        (await Status(client, ManagementPath, ("X-Test-Sub", "user-a"), ("X-Test-Ip", "10.0.0.1"))).ShouldBe(HttpStatusCode.OK);
        (await Status(client, ManagementPath, ("X-Test-Sub", "user-a"), ("X-Test-Ip", "10.0.0.2"))).ShouldBe(HttpStatusCode.OK);
        (await Status(client, ManagementPath, ("X-Test-Sub", "user-a"), ("X-Test-Ip", "10.0.0.3"))).ShouldBe(HttpStatusCode.TooManyRequests);
        (await Status(client, ManagementPath, ("X-Test-Sub", "user-b"), ("X-Test-Ip", "10.0.0.1"))).ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(ConnectPath)]
    [InlineData(AccountPath)]
    public async Task AuthIsStrictAndPartitionsByAddress(string path)
    {
        var client = await Start(Limits(delivery: 50, auth: 2));

        (await Status(client, path, ("X-Test-Ip", "10.0.0.1"), ("X-Test-Sub", "one"))).ShouldBe(HttpStatusCode.OK);
        (await Status(client, path, ("X-Test-Ip", "10.0.0.1"), ("X-Test-Sub", "two"))).ShouldBe(HttpStatusCode.OK);
        (await Status(client, path, ("X-Test-Ip", "10.0.0.1"), ("X-Test-Sub", "three"))).ShouldBe(HttpStatusCode.TooManyRequests);
        (await Status(client, path, ("X-Test-Ip", "10.0.0.2"), ("X-Test-Sub", "one"))).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TheDefaultAuthLimitIsLowerThanTheDefaultDeliveryAndManagementLimits()
    {
        await Start();

        var limits = _app!.Services.GetRequiredService<RateLimitOptions>();

        limits.Auth.PermitLimit.ShouldBeLessThan(limits.Delivery.PermitLimit);
        limits.Auth.PermitLimit.ShouldBeLessThan(limits.Management.PermitLimit);
    }

    [Fact]
    public async Task TheHealthGroupIsNotRateLimited()
    {
        var client = await Start(Limits(delivery: 1, auth: 1, management: 1));

        for (var request = 0; request < 5; request++)
        {
            using var response = await Send(client, "/health/probe");

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Headers.Contains("RateLimit-Limit").ShouldBeFalse();
        }
    }

    [Fact]
    public async Task ARateLimitedHostRefusesAZeroLimitWhenItStarts()
    {
        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => Start(Limits(delivery: 0)));

        exception.Code.ShouldBe("FCMS1101");
        exception.Message.ShouldContain("FluentCms:RateLimits:Delivery:PermitLimit");
    }

    [Fact]
    public async Task AnAllowedOriginGetsCorsHeadersAndAnotherGetsNone()
    {
        var client = await Start(Origins(delivery: "https://site.example.com"));

        using var allowed = await Send(client, DeliveryPath, ("Origin", "https://site.example.com"));
        using var other = await Send(client, DeliveryPath, ("Origin", "https://evil.example.com"));
        using var none = await Send(client, DeliveryPath);

        allowed.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe(["https://site.example.com"]);
        allowed.Headers.GetValues("Access-Control-Expose-Headers").Single().ShouldContain("RateLimit-Remaining");
        other.StatusCode.ShouldBe(HttpStatusCode.OK);
        other.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
        none.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    [Fact]
    public async Task WithNoOriginConfiguredNoOriginGetsCorsHeaders()
    {
        var client = await Start();

        foreach (var path in new[] { DeliveryPath, ManagementPath, ConnectPath })
        {
            using var response = await Send(client, path, ("Origin", "https://site.example.com"));

            response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse(path);
        }
    }

    [Fact]
    public async Task DeliveryAndManagementHaveSeparateAllowLists()
    {
        var client = await Start(Origins(delivery: "https://site.example.com", management: "https://admin.example.com"));

        using var siteOnDelivery = await Send(client, DeliveryPath, ("Origin", "https://site.example.com"));
        using var siteOnManagement = await Send(client, ManagementPath, ("Origin", "https://site.example.com"));
        using var adminOnManagement = await Send(client, ManagementPath, ("Origin", "https://admin.example.com"));
        using var adminOnDelivery = await Send(client, DeliveryPath, ("Origin", "https://admin.example.com"));
        using var adminOnConnect = await Send(client, ConnectPath, ("Origin", "https://admin.example.com"));

        siteOnDelivery.Headers.Contains("Access-Control-Allow-Origin").ShouldBeTrue();
        siteOnManagement.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
        adminOnManagement.Headers.Contains("Access-Control-Allow-Origin").ShouldBeTrue();
        adminOnDelivery.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
        adminOnConnect.Headers.Contains("Access-Control-Allow-Origin").ShouldBeTrue();
    }

    [Fact]
    public async Task APreflightFromAnAllowedOriginIsAnsweredAndOneFromAnotherIsNot()
    {
        var client = await Start(Origins(delivery: "https://site.example.com"));

        using var allowed = await Preflight(client, DeliveryPath, "https://site.example.com", "GET");
        using var other = await Preflight(client, DeliveryPath, "https://evil.example.com", "GET");
        using var method = await Preflight(client, DeliveryPath, "https://site.example.com", "DELETE");

        allowed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        allowed.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe(["https://site.example.com"]);
        other.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
        method.Headers.Contains("Access-Control-Allow-Methods").ShouldBeFalse();
    }

    [Fact]
    public async Task ARateLimitedResponseStillCarriesCorsHeadersSoTheBrowserCanReadIt()
    {
        var client = await Start(Limits(delivery: 1).Concat(Origins(delivery: "https://site.example.com")).ToDictionary());

        _ = await Status(client, DeliveryPath, ("Origin", "https://site.example.com"));
        using var refused = await Send(client, DeliveryPath, ("Origin", "https://site.example.com"));

        refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        refused.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe(["https://site.example.com"]);
    }

    [Fact]
    public async Task AWildcardOriginStopsTheHost()
    {
        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => Start(Origins(delivery: "*")));

        exception.Code.ShouldBe("FCMS1101");
    }

    [Theory]
    [InlineData(DeliveryPath)]
    [InlineData(ManagementPath)]
    [InlineData(ConnectPath)]
    [InlineData(AccountPath)]
    [InlineData("/health/probe")]
    public async Task FluentCmsResponsesCarryTheSecurityHeaders(string path)
    {
        var client = await Start();

        using var response = await Send(client, path);

        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.GetValues("Referrer-Policy").ShouldBe(["no-referrer"]);
    }

    [Fact]
    public async Task OnlyTheHostedPagesCarryFrameAncestors()
    {
        var client = await Start();

        using var account = await Send(client, AccountPath);
        using var delivery = await Send(client, DeliveryPath);
        using var connect = await Send(client, ConnectPath);

        account.Headers.GetValues("Content-Security-Policy").ShouldBe(["frame-ancestors 'none'"]);
        delivery.Headers.Contains("Content-Security-Policy").ShouldBeFalse();
        connect.Headers.Contains("Content-Security-Policy").ShouldBeFalse();
    }

    [Fact]
    public async Task FrameAncestorsAndReferrerPolicyComeFromOptions()
    {
        var client = await Start(new Dictionary<string, string?>
        {
            ["FluentCms:Security:FrameAncestors"] = "'self' https://admin.example.com",
            ["FluentCms:Security:ReferrerPolicy"] = "same-origin",
        });

        using var account = await Send(client, AccountPath);

        account.Headers.GetValues("Content-Security-Policy").ShouldBe(["frame-ancestors 'self' https://admin.example.com"]);
        account.Headers.GetValues("Referrer-Policy").ShouldBe(["same-origin"]);
    }

    [Theory]
    [InlineData("FrameAncestors", "'none'; script-src *")]
    [InlineData("FrameAncestors", "")]
    [InlineData("ReferrerPolicy", "sometimes")]
    public async Task ABadSecurityHeaderOptionStopsTheHost(string key, string value)
    {
        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() =>
            Start(new Dictionary<string, string?> { [$"FluentCms:Security:{key}"] = value }));

        exception.Code.ShouldBe("FCMS1101");
        exception.Message.ShouldContain(key);
    }

    [Fact]
    public async Task AnErrorResponseKeepsTheSecurityAndCorsHeaders()
    {
        var client = await Start(Origins(delivery: "https://site.example.com"));

        using var response = await Send(client, "/api/delivery/v1/boom", ("Origin", "https://site.example.com"));

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await Problem(response)).GetProperty("code").GetString().ShouldBe("FCMS3199");
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe(["https://site.example.com"]);
    }

    [Fact]
    public async Task AnEndpointOfTheHostItselfGetsNoFluentCmsHeaders()
    {
        var client = await Start();

        using var response = await Send(client, "/host");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains("X-Content-Type-Options").ShouldBeFalse();
        response.Headers.Contains("RateLimit-Limit").ShouldBeFalse();
    }

    [Fact]
    public async Task ABodyOverTheCapIsRefusedWithAProblem()
    {
        var client = await Start(new Dictionary<string, string?> { ["FluentCms:Limits:MaxBodySize"] = "100" });

        using var known = await Post(client, new string('a', 101), chunked: false);
        using var streamed = await Post(client, new string('a', 101), chunked: true);
        using var small = await Post(client, new string('a', 100), chunked: false);
        using var smallStreamed = await Post(client, new string('a', 100), chunked: true);

        foreach (var refused in new[] { known, streamed })
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
            refused.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
            (await Problem(refused)).GetProperty("code").GetString().ShouldBe("FCMS3111");
        }

        small.StatusCode.ShouldBe(HttpStatusCode.OK);
        smallStreamed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await small.Content.ReadAsStringAsync(Token)).ShouldBe("100");
    }

    [Fact]
    public async Task TheBodyCapDefaultsToTwoMegabytes()
    {
        await Start();

        _app!.Services.GetRequiredService<RequestLimitsOptions>().MaxBodySize.ShouldBe(2 * 1024 * 1024);
    }

    [Theory]
    [InlineData("size=500", "FCMS2207", "pageSize")]
    [InlineData("size=0", "FCMS2207", "pageSize")]
    [InlineData("depth=10", "FCMS2207", "depth")]
    [InlineData("nodes=500", "FCMS2207", "filter")]
    public async Task TheEngineCapsComeBackAsProblems(string query, string code, string path)
    {
        var client = await Start(content: true);

        using var response = await Send(client, $"/api/delivery/v1/probe/query?{query}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var problem = await Problem(response);
        problem.GetProperty("code").GetString().ShouldBe(code);
        problem.GetProperty("errors").EnumerateArray().Select(error => error.GetProperty("path").GetString()).ShouldContain(path);
    }

    [Fact]
    public async Task AQueryWithinTheCapsSucceeds()
    {
        var client = await Start(content: true);

        using var response = await Send(client, "/api/delivery/v1/probe/query?size=100&depth=3&nodes=10");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UseFluentCmsWithoutAddFluentCmsNamesTheMissingCall()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();

        var exception = Should.Throw<FluentCmsConfigurationException>(() => app.UseFluentCms());

        exception.Code.ShouldBe("FCMS1103");
    }

    internal static Dictionary<string, string?> Limits(int? delivery = null, int? management = null, int? auth = null)
    {
        Dictionary<string, string?> settings = [];
        if (delivery is not null)
        {
            settings["FluentCms:RateLimits:Delivery:PermitLimit"] = delivery.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (management is not null)
        {
            settings["FluentCms:RateLimits:Management:PermitLimit"] = management.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (auth is not null)
        {
            settings["FluentCms:RateLimits:Auth:PermitLimit"] = auth.Value.ToString(CultureInfo.InvariantCulture);
        }

        return settings;
    }

    internal static Dictionary<string, string?> Origins(string? delivery = null, string? management = null)
    {
        Dictionary<string, string?> settings = [];
        if (delivery is not null)
        {
            settings["FluentCms:Security:Cors:Delivery:AllowedOrigins:0"] = delivery;
        }

        if (management is not null)
        {
            settings["FluentCms:Security:Cors:Management:AllowedOrigins:0"] = management;
        }

        return settings;
    }

    internal static int Seconds(HttpResponseMessage response, string name) => int.Parse(Header(response, name)!, CultureInfo.InvariantCulture);

    internal static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.Single() : null;

    internal static async Task<JsonElement> Problem(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement.Clone();

    internal static async Task<HttpStatusCode> Status(HttpClient client, string path, params (string Name, string Value)[] headers)
    {
        using var response = await Send(client, path, headers);
        return response.StatusCode;
    }

    internal static Task<HttpResponseMessage> Send(HttpClient client, string path, params (string Name, string Value)[] headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        foreach (var (name, value) in headers)
        {
            request.Headers.Add(name, value);
        }

        return client.SendAsync(request, Token);
    }

    private static Task<HttpResponseMessage> Preflight(HttpClient client, string path, string origin, string method)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", method);
        return client.SendAsync(request, Token);
    }

    internal static Task<HttpResponseMessage> Post(HttpClient client, string body, bool chunked)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/management/v1/echo");
        request.Content = chunked
            ? new StreamContent(new ChunkedStream(Encoding.UTF8.GetBytes(body)))
            : new StringContent(body);
        return client.SendAsync(request, Token);
    }

    private async Task<HttpClient> Start(Dictionary<string, string?>? settings = null, bool content = false)
    {
        var app = _host.Build(
            Environments.Development,
            settings,
            builder =>
            {
                builder.Services.AddTestAuthentication();
                foreach (var group in Enum.GetValues<FluentCmsEndpointGroup>())
                {
                    builder.Services.AddFluentCmsEndpoints(group, routes =>
                    {
                        routes.MapGet("/probe", () => "ok");
                        routes.MapGet("/boom", IResult () => throw new InvalidOperationException("boom"));
                        routes.MapPost("/echo", async (HttpRequest request) =>
                        {
                            using var reader = new StreamReader(request.Body);
                            return (await reader.ReadToEndAsync()).Length.ToString(CultureInfo.InvariantCulture);
                        });
                    });
                }

                if (content)
                {
                    builder.Services.AddSingleton(new SchemaBuilder().AddContentType<Note>().Build());
                    builder.Services.AddFluentCmsEndpoints(FluentCmsEndpointGroup.Delivery, routes => routes.MapGet("/probe/query", QueryNotes));
                }
            },
            pipeline: app =>
            {
                app.Use((context, next) =>
                {
                    if (context.Request.Headers.TryGetValue("X-Test-Ip", out var address))
                    {
                        context.Connection.RemoteIpAddress = IPAddress.Parse(address.ToString());
                    }

                    return next(context);
                });
                app.UseFluentCms();
                app.MapGet("/host", () => "host");
            },
            cms: cms =>
            {
                if (content)
                {
                    cms.UseContentMigrations(typeof(CmsDbContext).Assembly);
                }
            });

        _app = app;
        await app.StartAsync(Token);
        return app.GetTestClient();
    }

    private static async Task<IResult> QueryNotes(IOperationContextAccessor accessor, IContentService<Note> notes, TimeProvider time, int? size, int? depth, int? nodes)
    {
        accessor.Current = new OperationContext(
            Principal.System,
            Origin.Seed,
            new SiteId(DefaultSite.Id),
            DefaultSite.Locale,
            ClientId: null,
            Guid.CreateVersion7().ToString(),
            time.GetUtcNow());

        ContentFilter? filter = nodes is null
            ? null
            : new And([.. Enumerable.Range(0, nodes.Value).Select(index => (ContentFilter)new FieldFilter("title", FilterOperator.Equal, index.ToString(CultureInfo.InvariantCulture)))]);
        var result = await notes.QueryAsync(new ContentQuery
        {
            Page = new PageRequest { Size = size },
            PopulateDepth = depth,
            Filter = filter,
        });
        return Results.Ok(result.Items.Count);
    }

    private sealed class ChunkedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;

        public override long Length => throw new NotSupportedException();
    }
}
