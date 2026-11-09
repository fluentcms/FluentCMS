using System.IO.Compression;
using System.Net;
using FluentCMS.Content;
using FluentCMS.Core.Api.Delivery;
using FluentCMS.Core.Caching;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Events;
using FluentCMS.Core.Identity;
using FluentCMS.Core.IntegrationTests.Hosting;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Events;
using FluentCMS.Testing;
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

    [Fact]
    public async Task WithTheOutputCacheOffAReadKeepsItsHeadersAndIsNotStored()
    {
        await using var host = new FluentCmsTestHost()
            .WithContentTypes<Note, Topic>()
            .Configure(cms => cms.DeliveryCache.OutputCache = false)
            .Seed(async services =>
            {
                var context = services.GetRequiredService<CmsDbContext>();
                context.Set<RoleGrant>().Add(new RoleGrant { Id = Guid.CreateVersion7(), RoleId = DefaultSite.PublicRole, Permission = "content.note.read" });
                await context.SaveChangesAsync();

                var notes = services.GetRequiredService<IContentService<Note>>();
                await notes.PublishAsync((await notes.CreateAsync(new Note { Title = "First", Slug = "first" })).Id);
            });
        using var client = host.CreateClient();

        using var first = await client.GetAsync(new Uri("/api/delivery/v1/note", UriKind.Relative), Token);
        using var second = await client.GetAsync(new Uri("/api/delivery/v1/note", UriKind.Relative), Token);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        first.Headers.ETag.ShouldNotBeNull();
        first.Headers.NonValidated["Cache-Control"].ToString().ShouldBe(DeliveryCacheOptions.DefaultCacheControl);
        second.Headers.Age.ShouldBeNull();
        host.Services.GetRequiredService<OutboxRegistry>().ForEvent(typeof(EntryPublishedEvent)).ShouldNotContain(registration => registration.Handler == typeof(OutputCacheEviction));
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

    // UseFluentCms authorizes before the output cache, so a policy on MapFluentCms holds for a kept copy as for a fresh one,
    // and a host that still calls UseAuthorization after it gets the same answers.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AHostPolicyOnMapFluentCmsHoldsForACachedRead(bool callsUseAuthorization)
    {
        var client = await StartAsync(
            app =>
            {
                app.UseFluentCms();
                if (callsUseAuthorization)
                {
                    app.UseAuthorization();
                }
            },
            builder => builder.Services.AddAuthorizationBuilder().AddPolicy("cms", policy => policy
                .AddAuthenticationSchemes(FluentCmsAuthentication.Scheme)
                .RequireAssertion(context => context.Resource is HttpContext http && http.Request.Headers.ContainsKey("X-Allowed"))),
            conventions => conventions.RequireAuthorization("cms"));

        using var allowed = await GetAsync(client, ("X-Allowed", "1"));
        using var cached = await GetAsync(client, ("X-Allowed", "1"));
        using var refused = await GetAsync(client);

        allowed.StatusCode.ShouldBe(HttpStatusCode.OK);
        cached.StatusCode.ShouldBe(HttpStatusCode.OK);
        cached.Headers.Age.ShouldNotBeNull();
        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        refused.Headers.Age.ShouldBeNull();
    }

    // Compression that wraps UseFluentCms encodes each answer for the request it goes to from the one copy the cache keeps
    // as the endpoint wrote it, so the key does not vary on Accept-Encoding.
    [Fact]
    public async Task CompressionBeforeUseFluentCmsEncodesEachAnswerFromOneCopy()
    {
        var client = await StartAsync(
            app =>
            {
                app.UseResponseCompression();
                app.UseFluentCms();
            },
            builder => builder.Services.AddResponseCompression());

        using var gzip = await GetAsync(client, ("Accept-Encoding", "gzip"));
        using var plain = await GetAsync(client);
        using var again = await GetAsync(client, ("Accept-Encoding", "gzip"));
        using var head = await SendAsync(client, HttpMethod.Head);

        gzip.Content.Headers.ContentEncoding.ShouldBe(["gzip"]);
        gzip.Headers.Vary.ShouldContain("Accept-Encoding");
        plain.Headers.Age.ShouldNotBeNull();
        plain.Content.Headers.ContentEncoding.ShouldBeEmpty();
        again.Headers.Age.ShouldNotBeNull();
        again.Content.Headers.ContentEncoding.ShouldBe(["gzip"]);
        (await DecompressAsync(again)).ShouldBe(await plain.Content.ReadAsStringAsync(Token));
        head.StatusCode.ShouldBe(HttpStatusCode.OK);
        head.Content.Headers.ContentLength.ShouldBe(plain.Content.Headers.ContentLength);
        (await head.Content.ReadAsByteArrayAsync(Token)).ShouldBeEmpty();
    }

    // Inside the cache, a body compressed for one request would be served to every other, so it is not kept.
    [Fact]
    public async Task ABodyCompressedInsideTheOutputCacheIsNotKept()
    {
        var client = await StartAsync(
            app =>
            {
                app.UseFluentCms();
                app.UseResponseCompression();
            },
            builder => builder.Services.AddResponseCompression());

        using var gzip = await GetAsync(client, ("Accept-Encoding", "gzip"));
        using var plain = await GetAsync(client);
        using var again = await GetAsync(client);

        gzip.Content.Headers.ContentEncoding.ShouldBe(["gzip"]);
        plain.Headers.Age.ShouldBeNull();
        plain.Content.Headers.ContentEncoding.ShouldBeEmpty();
        again.Headers.Age.ShouldNotBeNull();
    }

    // A host of its own with one content type that the Public role reads, so that an empty list is a read the cache keeps.
    private async Task<HttpClient> StartAsync(Action<WebApplication> pipeline, Action<WebApplicationBuilder> configure, Action<IEndpointConventionBuilder>? endpoints = null)
    {
        var app = _host.Build(
            Environments.Development,
            configure: builder =>
            {
                builder.Services.AddSingleton(new SchemaBuilder().AddContentType<Note>().AddContentType<Topic>().Build());
                configure(builder);
            },
            pipeline: pipeline,
            cms: cms =>
            {
                cms.UseContentMigrations(typeof(CmsDbContext).Assembly);
                cms.Jobs.Options.Queues[OutboxDrainJob.Queue] = 0;
            },
            endpoints: endpoints);
        await app.StartAsync(Token);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            context.Set<RoleGrant>().Add(new RoleGrant { Id = Guid.CreateVersion7(), RoleId = DefaultSite.PublicRole, Permission = "content.note.read" });
            await context.SaveChangesAsync(Token);
        }

        // The grant evicts the site's copies through the outbox. A worker would deliver that whenever it gets to it, which on
        // a busy machine can fall between a read that stores a copy and the next, so the test delivers it before any read.
        while (await app.Services.GetRequiredService<OutboxDrainer>().DrainAsync(Token) > 0)
        {
        }

        return app.GetTestClient();
    }

    private static Task<HttpResponseMessage> GetAsync(HttpClient client, params (string Name, string Value)[] headers) => SendAsync(client, HttpMethod.Get, headers);

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, params (string Name, string Value)[] headers)
    {
        using var request = new HttpRequestMessage(method, new Uri("/api/delivery/v1/note", UriKind.Relative));
        foreach (var (name, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        return await client.SendAsync(request, Token);
    }

    private static async Task<string> DecompressAsync(HttpResponseMessage response)
    {
        await using var gzip = new GZipStream(await response.Content.ReadAsStreamAsync(Token), CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        return await reader.ReadToEndAsync(Token);
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
