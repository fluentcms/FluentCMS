using FluentCMS.Content;
using FluentCMS.Core.Events;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Data.Tests.Migrations;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Plugins;
using FluentCMS.Plugins.Search;
using FluentCMS.Schema;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Query;

// The search plugin on the provider of the run: its migration set with the full-text objects of the provider, the match of
// each provider, and the rebuild, whose writes and removals are plain EF. The plugin's own tests run the rest on SQLite.
public sealed class SearchIndexProviderTests : IDisposable
{
    private static readonly SchemaModel Schema = SampleSchema.NewBuilder()
        .AddCollection("article", type => type
            .Localized()
            .Field("title", "text", field => field.Required().Localizable().Searchable())
            .Field("body", "richText", field => field.Localizable().Searchable())
            .Field("notes", "text"))
        .AddCollection("tag", type => type.Scope(ContentScope.Global).Field("name", "text", field => field.Searchable()))
        .Build();

    private readonly TestDatabase _database = TestDatabase.Create();
    private readonly IHost _host;

    public SearchIndexProviderTests()
    {
        _host = _database.CreateHost(Schema, "Production", GeneratedMigrations.CompileFirst(Schema), configure: cms => cms.AddPlugin<SearchPlugin>());
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _host.Dispose();
        _database.Dispose();
    }

    [Fact]
    public async Task TheSetOfTheProviderAddsItsFullTextObjects()
    {
        await MigrateAsync();

        if (TestProvider.IsSqlite)
        {
            _database.ReadStrings("select name from sqlite_master where name = 'cms_search_entry_fts'").ShouldBe([SearchModel.FtsTable]);
        }
        else if (TestProvider.IsPostgreSql)
        {
            _database.ReadStrings("select indexdef from pg_indexes where indexname = 'ix_cms_search_entry_text'").ShouldHaveSingleItem().ShouldContain("gin");
        }
        else
        {
            // No full-text catalogue: the table alone, matched with LIKE.
            _database.ReadStrings("select name from sys.tables where name like 'cms_search%'").ShouldBe([SearchModel.EntryTable]);
        }
    }

    [Fact]
    public async Task EveryWordMatchesAndTheLastAsTheStartOfAWordOrOnSqlServerAsAPartOfTheText()
    {
        await MigrateAsync();
        var learning = await CreateAsync("Learning dotnet search", "The body speaks of Kubernetes", publish: true);
        var garden = await CreateAsync("Gardening in spring", "Plain soil", publish: true);
        var draft = await CreateAsync("Café drafts", "Unpublished", publish: false);
        await DrainAsync();

        (await FindAsync("dotnet")).ShouldBe([learning]);
        (await FindAsync("DOT")).ShouldBe([learning]);
        (await FindAsync("learning dot")).ShouldBe([learning]);
        (await FindAsync("kubernetes")).ShouldBe([learning]);
        (await FindAsync("dotnet gardening")).ShouldBeEmpty();
        (await FindAsync("spring")).ShouldBe([garden]);
        (await FindAsync("cafe")).ShouldBeEmpty();
        (await FindAsync("cafe", QueryStatus.Draft)).ShouldBe([draft]);
        (await FindAsync("CAFÉ dr", QueryStatus.Draft)).ShouldBe([draft]);

        if (TestProvider.IsSqlServer)
        {
            (await FindAsync("otnet")).ShouldBe([learning]);
        }
        else
        {
            (await FindAsync("otnet")).ShouldBeEmpty();
            (await FindAsync("dot learning")).ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task TheRebuildWritesTheIndexAgainAndASecondRunChangesNothing()
    {
        await MigrateAsync();
        var article = await CreateAsync("Rebuilt words", "Body", publish: true);
        await DrainAsync();
        var indexed = await RowsAsync();
        indexed.Count.ShouldBe(2);

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<SearchPlugin>>();
            await database.Set<SearchEntry>().ExecuteDeleteAsync(Token);
            database.Add(new SearchEntry { DocumentId = Guid.CreateVersion7(), Locale = "en", State = SearchStates.Draft, Type = "article", SiteId = DefaultSite.Id, Text = "gone", UpdatedAt = DateTimeOffset.UnixEpoch });
            database.Add(new SearchEntry { DocumentId = Guid.CreateVersion7(), Locale = "", State = SearchStates.Draft, Type = "removed", SiteId = null, Text = "gone", UpdatedAt = DateTimeOffset.UnixEpoch });
            await database.SaveChangesAsync(Token);
        }

        (await RebuildAsync()).ShouldBe((2, 2));
        (await RowsAsync()).Select(row => (row.DocumentId, row.Locale, row.State, row.Text)).ShouldBe(indexed.Select(row => (row.DocumentId, row.Locale, row.State, row.Text)), ignoreOrder: true);
        (await FindAsync("rebuilt")).ShouldBe([article]);

        (await RebuildAsync()).ShouldBe((0, 0));
    }

    private async Task MigrateAsync() => await _host.Services.GetRequiredService<MigrationRunner>().MigrateAsync(Token);

    private async Task<Guid> CreateAsync(string title, string body, bool publish) => await AsSystemAsync(async content =>
    {
        var entry = new ContentDocument { ["title"] = title, ["body"] = new RichText($$"""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"{{body}}"}]}]}""") };
        var id = (Guid)(await content.CreateAsync("article", entry, Token))["id"]!;
        if (publish)
        {
            await content.PublishAsync("article", id, cancellationToken: Token);
        }

        return id;
    });

    private async Task<List<Guid>> FindAsync(string text, QueryStatus status = QueryStatus.Published) => await AsSystemAsync(async content =>
    {
        var page = await content.QueryAsync("article", new ContentQuery { Search = text, Status = status, Locale = "en" }, Token);
        return page.Items.Select(item => (Guid)item["id"]!).ToList();
    });

    private async Task<T> AsSystemAsync<T>(Func<IContentService, Task<T>> work)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        using var system = SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), Origin.Seed, new SiteId(DefaultSite.Id));
        return await work(scope.ServiceProvider.GetRequiredService<IContentService>());
    }

    private async Task DrainAsync()
    {
        while (await _host.Services.GetRequiredService<OutboxDrainer>().DrainAsync(Token) > 0)
        {
        }
    }

    private async Task<(int Written, int Removed)> RebuildAsync()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        using var system = SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), Origin.Job, new SiteId(DefaultSite.Id));
        return await scope.ServiceProvider.GetRequiredService<SearchIndexer>().RebuildAsync(null, new SiteId(DefaultSite.Id), Token);
    }

    private async Task<List<SearchEntry>> RowsAsync()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PluginDbContext<SearchPlugin>>().Set<SearchEntry>().AsNoTracking().ToListAsync(Token);
    }
}
