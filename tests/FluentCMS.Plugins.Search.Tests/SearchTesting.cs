using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Events;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Plugins;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using Role = FluentCMS.Core.Storage.Entities.Role;
using RoleGrant = FluentCMS.Core.Storage.Entities.RoleGrant;

namespace FluentCMS.Plugins.Search.Tests;

// A host with the search plugin, discovered, on four types, and a row rule that lets through articles with 5 views or
// more. Its job runner has no workers, so a test runs the outbox and the jobs itself, and an entry is found only once the
// test has delivered its events.
public sealed class SearchHost : IAsyncDisposable
{
    public const string PopularRule = "popular";

    public SearchHost(Action<FluentCmsTestHost>? seed = null)
    {
        Host = new FluentCmsTestHost()
            .WithContentTypes(typeof(Article), typeof(Note), typeof(Tag), typeof(Term))
            .WithServices(services => services.AddSingleton<ILoggerProvider>(Logs))
            .Configure(cms =>
            {
                cms.Jobs.Options.MaintenanceInterval = TimeSpan.FromDays(1);
                cms.Jobs.Options.Queues[JobRunnerOptions.DefaultQueue] = 0;
                cms.Jobs.Options.Queues[OutboxDrainJob.Queue] = 0;
                cms.Authorization.AddRowRule<Article>(PopularRule, _ => article => article.Views >= 5);
            });
        seed?.Invoke(Host);
    }

    public FluentCmsTestHost Host { get; }

    public IServiceProvider Services => Host.Services;

    public LogCapture Logs { get; } = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // An editor of Super Admin, through a Management key, in the default site or the one named.
    public async Task<TestActor> EditorAsync(Guid? site = null)
    {
        var actor = Host.As(await Host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token));
        return site is { } id ? actor.In(new SiteId(id)) : actor;
    }

    // A client of the Management or the Delivery API whose every request names the site.
    public async Task<HttpClient> ClientAsync(ApiKeyClass keyClass, string role = BuiltInRoles.SuperAdminName, Guid? site = null)
    {
        var client = Host.AsKey(await Host.CreateApiKeyAsync(keyClass, role, cancellationToken: Token));
        client.DefaultRequestHeaders.Add("X-Cms-Site", (site ?? DefaultSite.Id).ToString());
        return client;
    }

    // A new site with "en" and these locales.
    public async Task<Guid> SiteAsync(params string[] locales)
    {
        await using var scope = Services.CreateAsyncScope();
        var sites = scope.ServiceProvider.GetRequiredService<SiteManager>();
        var site = (await sites.CreateAsync("Docs", $"docs-{Guid.NewGuid():N}", "en", Token)).Id;
        foreach (var locale in locales)
        {
            await sites.EnsureLocaleAsync(site, locale, Token);
        }

        return site;
    }

    public async Task AddLocaleAsync(Guid site, string locale)
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SiteManager>().EnsureLocaleAsync(site, locale, Token);
    }

    public async Task DisableLocaleAsync(Guid site, string locale)
    {
        await using var scope = Services.CreateAsyncScope();
        using var system = SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), Origin.Seed, new SiteId(DefaultSite.Id));
        await scope.ServiceProvider.GetRequiredService<SiteManager>().UpdateLocaleAsync(site, locale, new SiteLocaleChange { Enabled = false }, Token);
    }

    // A reader through a Delivery key whose one grant reads articles under the row rule.
    public async Task<TestActor> RuledReaderAsync()
    {
        var role = new Role { Id = Guid.CreateVersion7(), Name = $"ruled-{Guid.NewGuid():N}" };
        await using (var scope = Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            context.Set<Role>().Add(role);
            context.Set<RoleGrant>().Add(new RoleGrant { Id = Guid.CreateVersion7(), RoleId = role.Id, Permission = "content.article.read", Constraints = new PermissionConstraints { RowRule = PopularRule }.ToJson() });
            await context.SaveChangesAsync(Token);
        }

        return Host.As(await Host.CreateApiKeyAsync(ApiKeyClass.Delivery, role.Name, cancellationToken: Token));
    }

    public async Task SetEnabledAsync(Guid site, bool enabled)
    {
        await using var scope = Services.CreateAsyncScope();
        using var system = SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), Origin.Seed, new SiteId(DefaultSite.Id));
        await scope.ServiceProvider.GetRequiredService<PluginAdministration>().SetEnabledAsync(SearchPlugin.Name, site, enabled, Token);
    }

    // Rounds of the outbox and of the due jobs until neither has anything left.
    public async Task PumpAsync()
    {
        for (var round = 0; round < 20; round++)
        {
            var delivered = await Services.GetRequiredService<OutboxDrainer>().DrainAsync(Token);
            var ran = 0;
            while (await RunNextJobAsync())
            {
                ran++;
            }

            if (delivered == 0 && ran == 0)
            {
                return;
            }
        }
    }

    internal async Task<List<SearchEntry>> RowsAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PluginDbContext<SearchPlugin>>().Set<SearchEntry>().AsNoTracking().OrderBy(entry => entry.Id).ToListAsync(Token);
    }

    internal async Task ClearIndexAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PluginDbContext<SearchPlugin>>().Set<SearchEntry>().ExecuteDeleteAsync(Token);
    }

    internal async Task AddRowAsync(SearchEntry entry)
    {
        await using var scope = Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<SearchPlugin>>();
        database.Add(entry);
        await database.SaveChangesAsync(Token);
    }

    public ValueTask DisposeAsync() => Host.DisposeAsync();

    private async Task<bool> RunNextJobAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<JobExecutor>().RunNextAsync(JobRunnerOptions.DefaultQueue, "test", Token);
    }
}

internal static class SearchTesting
{
    public const string Rebuild = "/api/management/v1/search/rebuild";

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    // The ids a list of the type finds for the text, as the actor reads it.
    public static async Task<List<Guid>> FindAsync(TestActor actor, string type, string text, string? locale = "en", QueryStatus status = QueryStatus.Published, ContentFilter? filter = null)
    {
        var page = await actor.Content().QueryAsync(type, new ContentQuery { Search = text, Locale = locale, Status = status, Filter = filter, Page = new PageRequest { Size = 100 } }, Token);
        return [.. page.Items.Select(item => (Guid)item["id"]!)];
    }

    // A rich text document of one paragraph per text.
    public static Schema.RichText Paragraphs(params string[] texts) => new(new JsonObject
    {
        ["type"] = "doc",
        ["content"] = new JsonArray([.. texts.Select(text => (JsonNode)new JsonObject
        {
            ["type"] = "paragraph",
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        })]),
    }.ToJsonString());

    // The body of an answer with the status expected.
    public static async Task<JsonNode> JsonAsync(HttpResponseMessage response, System.Net.HttpStatusCode expected)
    {
        var text = await response.Content.ReadAsStringAsync(Token);
        response.StatusCode.ShouldBe(expected, text);
        return JsonNode.Parse(text)!;
    }

    public static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object? body = null) =>
        body is null
            ? await client.PostAsync(new Uri(path, UriKind.Relative), null, Token)
            : await client.PostAsJsonAsync(new Uri(path, UriKind.Relative), body, Token);
}

public sealed class LogCapture : ILoggerProvider
{
    private readonly ConcurrentQueue<(int Id, string Line)> _lines = new();

    public IReadOnlyList<string> Of(int eventId) => [.. _lines.Where(line => line.Id == eventId).Select(line => line.Line)];

    public ILogger CreateLogger(string categoryName) => new Logger(this);

    public void Dispose()
    {
    }

    private sealed class Logger(LogCapture capture) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            capture._lines.Enqueue((eventId.Id, $"{eventId.Id} {formatter(state, exception)} {exception}"));
    }
}
