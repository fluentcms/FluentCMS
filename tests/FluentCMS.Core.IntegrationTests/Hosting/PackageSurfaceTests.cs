using System.Net;
using FluentCMS.Core.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Hosting;

// A package that maps a surface of its own with the public members of Core alone, as FluentCMS.Admin.Hosting does (#3451).
public sealed class PackageSurfaceTests : IAsyncDisposable
{
    private const string Policy = "default-src 'none'; frame-ancestors 'none'";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly CmsWebHost _host = new();

    private WebApplication? _app;

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task TheSurfaceGetsTheHeadersOfFluentCmsAndItsPagesTheirPolicy()
    {
        var client = await StartAsync();

        using var page = await client.GetAsync(new Uri("/things/", UriKind.Relative), Token);
        page.StatusCode.ShouldBe(HttpStatusCode.OK);
        page.Headers.GetValues("Content-Security-Policy").ShouldHaveSingleItem().ShouldBe(Policy);
        page.Headers.GetValues("X-Content-Type-Options").ShouldHaveSingleItem().ShouldBe("nosniff");
        page.Headers.GetValues("Referrer-Policy").ShouldHaveSingleItem().ShouldBe("no-referrer");
        page.Headers.CacheControl!.NoStore.ShouldBeTrue();

        using var file = await client.GetAsync(new Uri("/things/app.js", UriKind.Relative), Token);
        file.StatusCode.ShouldBe(HttpStatusCode.OK);
        file.Headers.Contains("Content-Security-Policy").ShouldBeFalse();
        file.Headers.GetValues("X-Content-Type-Options").ShouldHaveSingleItem().ShouldBe("nosniff");

        // An endpoint of the host's own, outside the surface, is left alone.
        using var own = await client.GetAsync(new Uri("/own", UriKind.Relative), Token);
        own.Headers.Contains("X-Content-Type-Options").ShouldBeFalse();
    }

    // The files count under their own limit and leave the coarse one, which the pages share with the APIs, as it was.
    [Fact]
    public async Task TheFilesCountUnderTheStaticFilesLimitAndThePagesUnderTheCoarseOne()
    {
        var client = await StartAsync(new Dictionary<string, string?>
        {
            ["FluentCms:RateLimits:PreAuth:PermitLimit"] = "2",
            ["FluentCms:RateLimits:Delivery:PermitLimit"] = "2",
            ["FluentCms:RateLimits:Delivery:MaxKeyLimit"] = "2",
            ["FluentCms:RateLimits:StaticFiles:PermitLimit"] = "5",
        });

        for (var request = 0; request < 5; request++)
        {
            (await StatusAsync(client, "/things/app.js")).ShouldBe(HttpStatusCode.OK);
        }

        (await StatusAsync(client, "/things/app.js")).ShouldBe(HttpStatusCode.TooManyRequests);
        (await StatusAsync(client, "/things/")).ShouldBe(HttpStatusCode.OK);
        (await StatusAsync(client, "/things/other")).ShouldBe(HttpStatusCode.OK);
        (await StatusAsync(client, "/things/")).ShouldBe(HttpStatusCode.TooManyRequests);
        (await StatusAsync(client, "/api/delivery/v1/missing")).ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task TheOptionsBindTheirSectionOverWhatCodeSet()
    {
        await StartAsync(new Dictionary<string, string?> { ["FluentCms:Things:Count"] = "3" });

        var options = _app!.Services.GetRequiredService<ThingOptions>();
        options.Count.ShouldBe(3);
        options.Name.ShouldBe("set in code");
    }

    [Theory]
    [InlineData("0", "FCMS1101: FluentCms:Things:Count is 0; it must be at least 1.")]
    [InlineData("many", "FCMS1101: ")]
    public async Task AnOptionTheValidateRefusesStopsTheHostWithItsKey(string count, string message)
    {
        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => StartAsync(new Dictionary<string, string?> { ["FluentCms:Things:Count"] = count }));

        exception.Code.ShouldBe("FCMS1101");
        exception.Message.ShouldStartWith(message);
    }

    [Theory]
    [InlineData("FluentCms")]
    [InlineData("fluentcms:Things")]
    public void ASectionThatNamesTheRootAgainIsRefused(string section)
    {
        var services = new ServiceCollection();

        Should.Throw<ArgumentException>(() => services.AddFluentCmsOptions(section, new ThingOptions())).ParamName.ShouldBe("section");
    }

    [Fact]
    public async Task MappingBeforeUseFluentCmsStopsTheHost()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();

        var exception = Should.Throw<FluentCmsConfigurationException>(() => app.EnsureFluentCmsPipeline("MapThings"));

        exception.Code.ShouldBe("FCMS1104");
        exception.Message.ShouldBe("FCMS1104: MapThings found no FluentCMS pipeline. Call app.UseFluentCms() before app.MapThings().");
        FluentCmsConfigurationException.NotRegistered("MapThings found no things.").Message.ShouldBe("FCMS1103: MapThings found no things.");
    }

    [Theory]
    [InlineData("delivery")]
    [InlineData("Management")]
    [InlineData("health")]
    [InlineData("docs")]
    public async Task ASurfaceOfCoreIsNotAPackagesName(string name)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();

        Should.Throw<ArgumentException>(() => app.MapGroup("/things").WithFluentCmsSurface(name)).ParamName.ShouldBe("name");
    }

    private async Task<HttpClient> StartAsync(Dictionary<string, string?>? settings = null)
    {
        _app = _host.Build(
            Environments.Development,
            settings,
            configure: builder =>
            {
                // A bucket the test empties never refills between its requests, however slow the machine.
                builder.Services.AddSingleton<TimeProvider>(new FrozenTime());
                builder.Services.AddFluentCmsOptions("Things", new ThingOptions { Name = "set in code" }, options => options.Validate());
            },
            pipeline: app =>
            {
                app.UseFluentCms();
                app.EnsureFluentCmsPipeline("MapThings");
                var things = app.MapGroup("/things").WithFluentCmsSurface("things");
                things.MapGet("/", () => "page").WithContentSecurityPolicy(_ => Policy);
                things.MapGet("/other", () => "page");
                things.MapGet("/app.js", () => "file").WithFluentCmsStaticFiles();
                app.MapGet("/own", () => "own");
            });
        await _app.StartAsync(Token);
        return _app.GetTestClient();
    }

    private static async Task<HttpStatusCode> StatusAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Token);
        return response.StatusCode;
    }

    public sealed class ThingOptions
    {
        public int Count { get; set; } = 1;

        public string? Name { get; set; }

        internal void Validate()
        {
            if (Count < 1)
            {
                throw FluentCmsConfigurationException.InvalidOption($"{nameof(Count)} is {Count}; it must be at least 1.");
            }
        }
    }
}
