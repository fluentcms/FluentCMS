using System.Net;
using FluentCMS.Core.Api.Delivery;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.IntegrationTests.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Delivery;

// FluentCms:Delivery:Cache as a host configures it, and the output cache as a host that uses it for itself sees it.
[Xunit.Collection(ProcessEnvironment.Collection)]
public sealed class DeliveryCacheHostTests : IAsyncDisposable
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly CmsWebHost _host = new();

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task TheOptionsBindFromConfiguration()
    {
        var app = _host.Build(Environments.Development, new Dictionary<string, string?>
        {
            ["FluentCms:Delivery:Cache:CacheControl"] = "public, max-age=5",
            ["FluentCms:Delivery:Cache:OutputCache"] = "false",
            ["FluentCms:Delivery:Cache:OutputCacheDuration"] = "00:00:30",
        });
        await app.StartAsync(Token);

        var options = app.Services.GetRequiredService<DeliveryCacheOptions>();
        options.CacheControl.ShouldBe("public, max-age=5");
        options.OutputCache.ShouldBeFalse();
        options.OutputCacheDuration.ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData("CacheControl", "max-age=soon")]
    [InlineData("CacheControl", " ")]
    [InlineData("OutputCacheDuration", "00:00:00")]
    public async Task ABadOptionStopsTheHost(string key, string value)
    {
        var app = _host.Build(Environments.Development, new Dictionary<string, string?> { [$"FluentCms:Delivery:Cache:{key}"] = value });

        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => app.StartAsync(Token));

        exception.Code.ShouldBe("FCMS1101");
        exception.Message.ShouldContain($"FluentCms:Delivery:Cache:{key}");
    }

    // A base policy of the host's that would cache anything reaches the host's own routes and none of FluentCMS's but Delivery.
    [Fact]
    public async Task OnlyDeliveryIsCachedUnderABasePolicyOfTheHost()
    {
        var app = _host.Build(
            Environments.Development,
            configure: builder => builder.Services.AddOutputCache(options => options.AddBasePolicy(new CacheEverything())));
        app.MapGet("/host", () => "host");
        await app.StartAsync(Token);
        var client = app.GetTestClient();

        await client.GetAsync(new Uri("/host", UriKind.Relative), Token);
        await client.GetAsync(new Uri("/health/live", UriKind.Relative), Token);
        using var host = await client.GetAsync(new Uri("/host", UriKind.Relative), Token);
        using var health = await client.GetAsync(new Uri("/health/live", UriKind.Relative), Token);

        host.Headers.Age.ShouldNotBeNull();
        health.StatusCode.ShouldBe(HttpStatusCode.OK);
        health.Headers.Age.ShouldBeNull();
    }

    private sealed class CacheEverything : IOutputCachePolicy
    {
        public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellationToken)
        {
            context.EnableOutputCaching = true;
            context.AllowCacheLookup = true;
            context.AllowCacheStorage = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
