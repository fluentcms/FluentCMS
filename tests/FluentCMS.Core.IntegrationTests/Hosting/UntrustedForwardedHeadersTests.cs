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

// Behind a proxy the host does not trust every caller shares the proxy's address, so the first request that shows it is
// reported. Each test has a host of its own, since the report is once per process of the host.
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
    public async Task WithForwardedHeadersOffTheFirstForwardedRequestIsReportedOnce()
    {
        var (app, client) = await StartAsync([]);

        await SendAsync(client, "172.18.0.5", ("X-Forwarded-For", "203.0.113.9"));
        await SendAsync(client, "172.18.0.6", ("X-Forwarded-Proto", "https"));

        var warning = Warnings().ShouldHaveSingleItem();
        warning.Level.ShouldBe(LogLevel.Warning);
        warning.Message.ShouldContain("172.18.0.5");
        warning.Message.ShouldContain("FluentCms:ReverseProxy:ForwardedHeaders:Enabled");
        Report(app).ShouldHaveSingleItem().Message.ShouldContain("forwarded headers are off");
    }

    [Theory]
    [InlineData("X-Forwarded-For", "203.0.113.9")]
    [InlineData("X-Forwarded-Proto", "https")]
    public async Task AForwardedRequestFromAnAddressThatIsNotListedIsReported(string header, string value)
    {
        var (app, client) = await StartAsync(new() { [Section + "Enabled"] = "true", [Section + "KnownProxies:0"] = "10.0.0.1" });

        await SendAsync(client, "10.0.0.2", (header, value));

        Warnings().ShouldHaveSingleItem().Message.ShouldContain("10.0.0.2");
        Report(app).ShouldHaveSingleItem().Message.ShouldContain("KnownProxies or KnownNetworks");
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
