using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Events;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Plugins.Media;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.MultiSite.Tests;

// A host with the multi-site, media, GraphQL and MCP plugins, all discovered, on a site-scoped and a global content type.
// Its media is in a directory of its own, removed with it, and no queue has workers, so a test drains the outbox itself.
// The tests of a class share one, each with sites and host names of its own.
public class MultiSiteHost : IAsyncDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("fluentcms-multi-site-");

    public MultiSiteHost()
        : this(null)
    {
    }

    protected MultiSiteHost(string? databaseFile, Action<FluentCmsBuilder>? configure = null)
    {
        Host = new FluentCmsTestHost()
            .WithContentTypes<Article, Tag>()
            .WithServices(services => services.PostConfigure<MediaOptions>(options => options.Local.Root = _directory.FullName))
            .Configure(cms =>
            {
                cms.Jobs.Options.Queues[JobRunnerOptions.DefaultQueue] = 0;
                cms.Jobs.Options.Queues[OutboxDrainJob.Queue] = 0;
                configure?.Invoke(cms);
            });
        if (databaseFile is not null)
        {
            Host.UseSqlite(databaseFile);
        }
    }

    public FluentCmsTestHost Host { get; }

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        _directory.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }
}

internal static class MultiSiteTesting
{
    public const string SitesRoute = "/api/management/v1/sites";

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    public static async Task<HttpClient> AdminAsync(FluentCmsTestHost host) =>
        host.AsKey(await host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token));

    // A host name of the test's own, since a host name is one site's in the whole instance.
    public static string HostName(string label = "www") => $"{label}-{Guid.NewGuid():N}.example.com";

    public static string Slug() => $"site-{Guid.NewGuid():N}";

    public static async Task<Guid> CreateSiteAsync(HttpClient admin, string? slug = null, string locale = "en")
    {
        var site = await SendAsync(admin, HttpMethod.Post, SitesRoute, new { name = "Second", slug = slug ?? Slug(), defaultLocale = locale }, HttpStatusCode.Created);
        return Guid.Parse(site!["id"]!.GetValue<string>());
    }

    public static Task<JsonNode?> AddHostAsync(HttpClient admin, Guid site, string hostname, bool? isPrimary = null, HttpStatusCode expected = HttpStatusCode.Created) =>
        SendAsync(admin, HttpMethod.Post, $"{SitesRoute}/{site}/hosts", new { hostname, isPrimary }, expected);

    // A new site with one host name, whose Public role reads articles and tags.
    public static async Task<(Guid Site, string HostName)> SiteWithHostAsync(FluentCmsTestHost host, HttpClient admin)
    {
        var site = await CreateSiteAsync(admin);
        var name = HostName();
        await AddHostAsync(admin, site, name);
        await LetThePublicReadAsync(host, site);
        return (site, name);
    }

    public static async Task LetThePublicReadAsync(FluentCmsTestHost host, Guid site)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        var role = await context.Set<Role>().SingleAsync(item => item.SiteId == site && item.Name == BuiltInRoles.PublicName, Token);
        context.Set<RoleGrant>().AddRange(
            new RoleGrant { Id = Guid.CreateVersion7(), RoleId = role.Id, Permission = "content.article.read" },
            new RoleGrant { Id = Guid.CreateVersion7(), RoleId = role.Id, Permission = "content.tag.read" });
        await context.SaveChangesAsync(Token);
    }

    // A published article of the site, written as an administrator of the instance.
    public static async Task<Guid> PublishArticleAsync(FluentCmsTestHost host, Guid site, string title)
    {
        var articles = host.As(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token)).In(new SiteId(site)).Content<Article>();
        var article = await articles.CreateAsync(new Article { Title = title }, Token);
        await articles.PublishAsync(article.Id, cancellationToken: Token);
        return article.Id;
    }

    // The titles a Delivery list of articles answers to a request whose Host header is the one given, as it is written,
    // and the response for its headers.
    public static async Task<(List<string> Titles, HttpResponseMessage Response)> ArticlesAsync(HttpClient client, string host, string query = "")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/api/delivery/v1/article{query}", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("Host", host).ShouldBeTrue();
        var response = await client.SendAsync(request, Token);
        var text = await response.Content.ReadAsStringAsync(Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        return ([.. JsonNode.Parse(text)!["items"]!.AsArray().Select(item => item!["title"]!.GetValue<string>())], response);
    }

    // A key of a new role with these permissions: a role of the site given, or of the instance, and a key bound to the
    // sites given, or to none. Written through the Management API, as an administrator would.
    public static async Task<HttpClient> RoleKeyAsync(FluentCmsTestHost host, HttpClient admin, string keyClass, Guid? roleSite, Guid[]? boundSites, params string[] permissions)
    {
        var name = "role-" + Guid.NewGuid().ToString("N");
        var role = await SendAsync(
            admin,
            HttpMethod.Post,
            "/api/management/v1/roles",
            new { name, siteId = roleSite, grants = permissions.Select(permission => new { permission }) },
            HttpStatusCode.Created);
        var key = await SendAsync(
            admin,
            HttpMethod.Post,
            "/api/management/v1/api-keys",
            new { name, @class = keyClass, roleId = role!["id"]!.GetValue<string>(), siteIds = boundSites },
            HttpStatusCode.Created);

        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", key!["secret"]!.GetValue<string>());
        return client;
    }

    public static async Task<JsonNode?> SendAsync(HttpClient client, HttpMethod method, string path, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.RelativeOrAbsolute));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await client.SendAsync(request, Token);
        var text = await response.Content.ReadAsStringAsync(Token);
        response.StatusCode.ShouldBe(expected, text);
        return text.Length == 0 ? null : JsonNode.Parse(text);
    }

    public static string? Code(JsonNode? problem) => problem?["code"]?.GetValue<string>();

    public static async Task DrainOutboxAsync(FluentCmsTestHost host)
    {
        while (await host.Services.GetRequiredService<OutboxDrainer>().DrainAsync(Token) > 0)
        {
        }
    }

    // The activity rows about the site, once the outbox has written those of its events: the action, the host name it
    // names, if any, and the site the row belongs to.
    public static async Task<List<(string Action, string? Hostname, Guid? Site)>> ActivityAsync(FluentCmsTestHost host, Guid site)
    {
        await DrainOutboxAsync(host);
        await using var scope = host.Services.CreateAsyncScope();
        var target = site.ToString("D");
        var rows = await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<ActivityRow>().AsNoTracking()
            .Where(row => row.TargetType == "site" && row.TargetId == target)
            .ToListAsync(Token);
        return [.. rows.Select(row => (row.Action, row.Details is null ? null : JsonNode.Parse(row.Details)?["hostname"]?.GetValue<string>(), row.SiteId))];
    }
}
