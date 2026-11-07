using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity.ApiKeys;
using FluentCMS.Core.Storage;
using FluentCMS.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using KeyClass = FluentCMS.Core.Identity.ApiKeys.ApiKeyClass;
using TestKeyClass = FluentCMS.Testing.ApiKeyClass;

namespace FluentCMS.Plugins.Tests;

// What a plugin's MapGroup endpoints get from core: a signed-in caller unless the endpoint allows anonymous, and the coarse
// limit before authentication (#2859).
public sealed class PluginEndpointTests
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnEndpointNeedsASignedInCallerUnlessItAllowsAnonymous()
    {
        await using var host = new FluentCmsTestHost();
        var key = await host.CreateApiKeyAsync(TestKeyClass.Delivery, BuiltInRoles.PublicName, cancellationToken: Token);
        using var anonymous = host.CreateClient();
        using var signedIn = host.AsKey(key);
        using var wrongKey = WithKey(host, ApiKeyFormat.Generate(KeyClass.Delivery));

        using var refused = await anonymous.GetAsync(new Uri("/sample/greeting", UriKind.Relative), Token);
        using var banner = await anonymous.GetAsync(new Uri("/sample/banner", UriKind.Relative), Token);
        using var greeting = await signedIn.GetAsync(new Uri("/sample/greeting", UriKind.Relative), Token);
        using var refusedKey = await wrongKey.GetAsync(new Uri("/sample/banner", UriKind.Relative), Token);

        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodeAsync(refused)).ShouldBe("FCMS3102");
        banner.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TextAsync(banner)).ShouldBe("discovered");
        greeting.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TextAsync(greeting)).ShouldBe("discovered");

        // Credentials that are sent and refused are never read as anonymous, not even where anonymous is allowed.
        refusedKey.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnEndpointMappedWithARequestDelegateNeedsASignedInCallerToo()
    {
        await using var host = new FluentCmsTestHost().Configure(cms => cms.AddPlugin<RoutedPlugin>());
        var key = await host.CreateApiKeyAsync(TestKeyClass.Delivery, BuiltInRoles.PublicName, cancellationToken: Token);
        using var anonymous = host.CreateClient();
        using var signedIn = host.AsKey(key);

        foreach (var path in new[] { "/routed/ping", "/routed/raw" })
        {
            using var refused = await anonymous.GetAsync(new Uri(path, UriKind.Relative), Token);
            using var allowed = await signedIn.GetAsync(new Uri(path, UriKind.Relative), Token);

            refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, path);
            allowed.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        }
    }

    [Fact]
    public async Task AFloodOfWellFormedKeysIsCutByTheCoarseLimitBeforeTheKeyLookup()
    {
        var lookups = new KeyLookups();
        await using var host = new FluentCmsTestHost()
            .WithServices(services => services.PostConfigure<RateLimitOptions>(limits =>
            {
                limits.PreAuth.PermitLimit = 3;
                limits.Delivery.PermitLimit = 3;
                limits.Delivery.MaxKeyLimit = 3;
            }))
            .Configure(cms => cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(lookups)));

        for (var request = 0; request < 3; request++)
        {
            using var client = WithKey(host, ApiKeyFormat.Generate(KeyClass.Delivery));
            using var answered = await client.GetAsync(new Uri("/sample/banner", UriKind.Relative), Token);
            answered.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        var before = lookups.Count;
        before.ShouldBeGreaterThan(0);
        using var flooded = WithKey(host, ApiKeyFormat.Generate(KeyClass.Delivery));
        using var refused = await flooded.GetAsync(new Uri("/sample/banner", UriKind.Relative), Token);

        refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await CodeAsync(refused)).ShouldBe("FCMS3110");
        lookups.Count.ShouldBe(before);
    }

    private static HttpClient WithKey(FluentCmsTestHost host, string secret)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", secret);
        return client;
    }

    private static async Task<string> CodeAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Token))!["code"]!.GetValue<string>();

    private static async Task<string> TextAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Token))!["text"]!.GetValue<string>();

    // Counts the queries of the API key table, which is what an API key lookup reads.
    private sealed class KeyLookups : DbCommandInterceptor
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Record(command);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Record(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Record(DbCommand command)
        {
            if (command.CommandText.Contains("\"cms_api_key\"", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _count);
            }
        }
    }
}
