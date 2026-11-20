using System.Net;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Diagnostics;
using FluentCMS.Core.IntegrationTests.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Hosting;

// Behind a proxy the host does not trust every caller shares the proxy's address, so requests that show it are reported,
// for the first few addresses. Each test has a host of its own, since each address is reported once per process.
public sealed class UntrustedForwardedHeadersTests : IAsyncDisposable
{
    private const string Section = "FluentCms:ReverseProxy:ForwardedHeaders:";
    private const string Untrusted = "forwarded-headers-untrusted";
    private const string TestAddress = "X-Test-Ip";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly CmsWebHost _host = new();
    private readonly LogCapture _logs = new();

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task WithForwardedHeadersOffEachAddressIsReportedOnce()
    {
        var (app, client) = await StartAsync([]);

        await SendAsync(client, "172.18.0.5", ("X-Forwarded-For", "203.0.113.9"));
        await SendAsync(client, "172.18.0.5", ("X-Forwarded-For", "203.0.113.10"));
        await SendAsync(client, "172.18.0.6", ("X-Forwarded-Proto", "https"));

        var warnings = Warnings();
        warnings.Count.ShouldBe(2);
        warnings.ShouldAllBe(warning => warning.Level == LogLevel.Warning);
        warnings[0].Message.ShouldStartWith("Insecure default forwarded-headers-untrusted from 172.18.0.5: A request from 172.18.0.5 ");
        warnings[1].Message.ShouldContain("A request from 172.18.0.6 ");
        warnings[0].Message.ShouldContain("FluentCms:ReverseProxy:ForwardedHeaders:Enabled");
        var item = Report(app).ShouldHaveSingleItem();
        item.Message.ShouldStartWith("Requests from 172.18.0.5, 172.18.0.6 carried");
        item.Message.ShouldContain("while forwarded headers are off");
    }

    // A forged header from a direct caller must neither be advised into KnownProxies nor hide the real proxy behind it.
    [Fact]
    public async Task AForgedHeaderIsNotAdvisedIntoTheTrustedListAndDoesNotHideALaterProxy()
    {
        var (app, client) = await StartAsync(new() { [Section + "Enabled"] = "true", [Section + "KnownProxies:0"] = "10.0.0.1" });

        await SendAsync(client, "198.51.100.7", ("X-Forwarded-For", "127.0.0.1"));
        await SendAsync(client, "172.18.0.5", ("X-Forwarded-For", "203.0.113.9"));

        Warnings().Select(warning => warning.State["Address"]).ShouldBe(["198.51.100.7", "172.18.0.5"]);
        var message = Report(app).ShouldHaveSingleItem().Message;
        message.ShouldContain("198.51.100.7, 172.18.0.5");
        message.ShouldContain("Only once you have confirmed that an address is the instance's own proxy, list it in KnownProxies or KnownNetworks.");
        message.ShouldContain("do not trust it, and close that route");
    }

    [Fact]
    public async Task OnlyTheFirstFewAddressesAreReported()
    {
        var (app, client) = await StartAsync([]);

        for (var last = 1; last <= 6; last++)
        {
            await SendAsync(client, $"172.18.0.{last}", ("X-Forwarded-For", "203.0.113.9"));
        }

        Warnings().Count.ShouldBe(4);
        var message = Report(app).ShouldHaveSingleItem().Message;
        message.ShouldStartWith("Requests from 172.18.0.1, 172.18.0.2, 172.18.0.3, 172.18.0.4 carried");
        message.ShouldEndWith("Only the first 4 addresses are named.");
        message.ShouldNotContain("172.18.0.5");
    }

    [Theory]
    [InlineData("X-Forwarded-For", "203.0.113.9")]
    [InlineData("X-Forwarded-Proto", "https")]
    public async Task AForwardedRequestFromAnAddressThatIsNotListedIsReported(string header, string value)
    {
        var (app, client) = await StartAsync(new() { [Section + "Enabled"] = "true", [Section + "KnownProxies:0"] = "10.0.0.1" });

        await SendAsync(client, "10.0.0.2", (header, value));

        Warnings().ShouldHaveSingleItem().Message.ShouldContain("A request from 10.0.0.2 ");
        Report(app).ShouldHaveSingleItem().Message.ShouldContain("from outside FluentCms:ReverseProxy:ForwardedHeaders:KnownProxies and KnownNetworks");
    }

    [Fact]
    public async Task AListedProxyAndARequestWithoutForwardedHeadersAreNotReported()
    {
        var (app, client) = await StartAsync(new() { [Section + "Enabled"] = "true", [Section + "KnownNetworks:0"] = "10.0.0.0/24" });

        await SendAsync(client, "10.0.0.7", ("X-Forwarded-For", "203.0.113.9"), ("X-Forwarded-Proto", "https"));
        await SendAsync(client, "::ffff:10.0.0.8", ("X-Forwarded-For", "203.0.113.9"));
        await SendAsync(client, "198.51.100.1");

        Warnings().ShouldBeEmpty();
        Report(app).ShouldBeEmpty();
    }

    // A header the options do not read is the operator's choice, not a proxy that was left out.
    [Fact]
    public async Task AHeaderThatIsNotForwardedIsNotReported()
    {
        var (app, client) = await StartAsync(new() { [Section + "Enabled"] = "true", [Section + "ForwardProto"] = "false" });

        await SendAsync(client, "172.18.0.5", ("X-Forwarded-Proto", "https"));

        Warnings().ShouldBeEmpty();
        Report(app).ShouldBeEmpty();
    }

    private List<LogEntry> Warnings() => [.. _logs.Entries.Where(entry => entry.EventId.Id == 9133)];

    private static List<FluentCMS.Diagnostics.InsecureDefault> Report(WebApplication app) =>
        [.. app.Services.GetRequiredService<StartupReportHolder>().Current!.InsecureDefaults.Where(item => item.Id == Untrusted)];

    private static async Task SendAsync(HttpClient client, string address, params (string Name, string Value)[] headers)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/host", UriKind.Relative));
        request.Headers.Add(TestAddress, address);
        foreach (var (name, value) in headers)
        {
            request.Headers.Add(name, value);
        }

        using var response = await client.SendAsync(request, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<(WebApplication App, HttpClient Client)> StartAsync(Dictionary<string, string?> settings)
    {
        var app = _host.Build(
            Environments.Development,
            settings,
            builder => builder.Logging.AddProvider(_logs),
            pipeline: app =>
            {
                app.Use((context, next) =>
                {
                    context.Connection.RemoteIpAddress = IPAddress.Parse(context.Request.Headers[TestAddress].ToString());
                    return next(context);
                });
                app.UseFluentCms();
                app.MapGet("/host", () => "host");
            });
        await app.StartAsync(Token);
        return (app, app.GetTestClient());
    }
}
