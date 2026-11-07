using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentCMS.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Webhooks.Tests.WebhookTesting;

namespace FluentCMS.Plugins.Webhooks.Tests;

// The target URL rules of #2837: https outside Development, no address that is not public, at save and at connect time, and
// no redirect followed.
public sealed class WebhookAddressTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.10.0.1")]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.10")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.0.0.5")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("64:ff9b::a00:5")]
    [InlineData("2002:a00:5::1")]
    [InlineData("2001:0:4136:e378:8000:63bf:3fff:fdd2")]
    [InlineData("192.0.0.8")]
    [InlineData("198.18.0.1")]
    public void AnAddressThatIsNotPublicIsRefused(string address) =>
        WebhookAddresses.IsPublic(IPAddress.Parse(address)).ShouldBeFalse();

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("172.32.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("::ffff:8.8.8.8")]
    [InlineData("64:ff9b::808:808")]
    [InlineData("2002:808:808::1")]
    public void APublicAddressIsAllowed(string address) =>
        WebhookAddresses.IsPublic(IPAddress.Parse(address)).ShouldBeTrue();

    [Theory]
    [InlineData("https://example.com/hooks", false, null)]
    [InlineData("http://example.com/hooks", true, null)]
    [InlineData("http://example.com/hooks", false, "https")]
    [InlineData("ftp://example.com/hooks", true, "https or http")]
    [InlineData("example.com/hooks", true, "absolute")]
    [InlineData("file:///etc/passwd", true, "https or http")]
    [InlineData("https://user:password@example.com/hooks", false, "user name")]
    public void AUrlNeedsHttpsOutsideDevelopment(string url, bool development, string? refusal)
    {
        var problem = WebhookAddresses.UrlRefusal(url, development);

        if (refusal is null)
        {
            problem.ShouldBeNull();
        }
        else
        {
            problem.ShouldNotBeNull().ShouldContain(refusal);
        }
    }

    [Theory]
    [InlineData("http://127.0.0.1:9/hooks")]
    [InlineData("http://10.0.0.5/hooks")]
    [InlineData("http://[::1]:9/hooks")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://localhost:9/hooks")]
    public async Task ATargetThatIsNotPublicIsRefusedWhenItIsSaved(string url)
    {
        await using var host = Host(allowPrivateNetworks: false);
        using var client = await AdminAsync(host);

        using var response = await client.PostAsJsonAsync(new Uri("/api/management/v1/webhooks", UriKind.Relative), new { name = "inside", url, events = new List<string> { "content.published" } }, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, await TextAsync(response));
        var error = JsonNode.Parse(await TextAsync(response))!["errors"]!.AsArray().ShouldHaveSingleItem()!;
        (error["path"]!.GetValue<string>(), error["code"]!.GetValue<string>()).ShouldBe(("url", WebhookErrorCodes.PrivateAddress));
    }

    // A webhook saved while its host was public, then pointed by DNS at the host's own network, is stopped at the connection.
    [Fact]
    public async Task ADeliveryToAnAddressThatIsNotPublicFailsWithoutARequestLeavingTheHost()
    {
        await using var receiver = await TestReceiver.StartAsync();
        await using var host = Host(allowPrivateNetworks: false);
        using var client = await AdminAsync(host);
        var webhook = await CreateAsync(client, new { name = "rebound", url = "https://example.com/hooks", events = new List<string> { "content.published" } });
        await PointAtAsync(host, Id(webhook), receiver.Url());

        using var ping = await client.PostAsync(new Uri($"/api/management/v1/webhooks/{Id(webhook):D}/ping", UriKind.Relative), null, Token);
        ping.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await PumpAsync(host);

        receiver.Requests.ShouldBeEmpty();
        var attempt = (await DeliveriesAsync(host)).ShouldHaveSingleItem();
        attempt.State.ShouldBe(DeliveryStates.Dead);
        attempt.StatusCode.ShouldBeNull();
        attempt.Error.ShouldBe(WebhookAddresses.NotPublic);
    }

    [Fact]
    public async Task ARedirectIsNotFollowed()
    {
        await using var receiver = await TestReceiver.StartAsync();
        await using var inside = await TestReceiver.StartAsync();
        receiver.RedirectTo = inside.Url("/latest/meta-data").ToString();
        await using var host = Host();
        using var client = await AdminAsync(host);
        var webhook = await CreateAsync(client, new { name = "moved", url = receiver.Url().ToString(), events = new List<string> { "content.published" } });

        using var ping = await client.PostAsync(new Uri($"/api/management/v1/webhooks/{Id(webhook):D}/ping", UriKind.Relative), null, Token);
        await PumpAsync(host);

        receiver.Requests.ShouldHaveSingleItem();
        inside.Requests.ShouldBeEmpty();
        var attempt = (await DeliveriesAsync(host)).ShouldHaveSingleItem();
        (attempt.State, attempt.StatusCode).ShouldBe((DeliveryStates.Dead, (int?)302));
        attempt.Error.ShouldNotBeNull().ShouldContain("redirects are not followed");
    }

    [Fact]
    public void AllowingPrivateNetworksIsAnInsecureDefaultInProductionOnly()
    {
        static IEnumerable<string> Reported(string environment, bool allow) =>
            new WebhookInsecureDefaults(Options.Create(new WebhooksOptions { AllowPrivateNetworks = allow }), new Environment(environment))
                .GetInsecureDefaults().Select(item => item.Id);

        Reported(Environments.Production, allow: true).ShouldBe([WebhookInsecureDefaults.PrivateNetworks]);
        Reported(Environments.Production, allow: false).ShouldBeEmpty();
        Reported(Environments.Development, allow: true).ShouldBeEmpty();
    }

    [Fact]
    public async Task TheHostAsksThePluginForItsInsecureDefaults()
    {
        await using var host = Host();

        host.Services.GetServices<IInsecureDefaultSource>().ShouldContain(source => source is WebhookInsecureDefaults);
    }

    private static async Task PointAtAsync(Testing.FluentCmsTestHost host, Guid id, Uri url)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<WebhooksPlugin>>();
        var webhook = await database.Set<Webhook>().FindAsync([id], Token);
        webhook!.Url = url.ToString();
        await database.SaveChangesAsync(Token);
    }

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
