using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Core.Events;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Data.Tests.Migrations;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Plugins;
using FluentCMS.Plugins.Search;
using FluentCMS.Providers;
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
            _database.ReadStrings("select name from sys.tables where name = 'cms_search_entry'").ShouldBe([SearchModel.EntryTable]);
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

    // The words of a search may be in different fields, but only in the fields the request names when it names them.
    [Fact]
    public async Task OnlyTheFieldsTheRequestNamesMatch()
    {
        await MigrateAsync();
        var article = await CreateAsync("Quarterly report", "Merger talks", publish: true);
        await DrainAsync();

        (await MatchAsync("quarterly merg", fields: null)).ShouldBe([article]);
        (await MatchAsync("quarterly merg", ["title", "body"])).ShouldBe([article]);
        (await MatchAsync("quarterly merg", ["title"])).ShouldBeEmpty();
        (await MatchAsync("merger", ["title"])).ShouldBeEmpty();
        (await MatchAsync("quart", ["title"])).ShouldBe([article]);
        (await MatchAsync("quarterly", [])).ShouldBeEmpty();
    }

    // Through the engine, as a caller limited to the title by its role, or by the role of its client: no entry and no count
    // for a word of the body, and the title still found.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AFieldLimitedCallerMatchesOnlyTheFieldsItMayRead(bool byClientRole)
    {
        await MigrateAsync();
        var article = await CreateAsync("Quarterly report", "Merger talks", publish: true);
        await DrainAsync();
        var titleOnly = await RoleAsync(new PermissionConstraints { Fields = ["title"] });
        var principal = byClientRole ? Caller(await RoleAsync(null), titleOnly) : Caller(titleOnly);

        (await FindAsAsync(principal, "quarterly")).Items.Select(item => (Guid)item["id"]!).ShouldBe([article]);
        var hidden = await FindAsAsync(principal, "merger");
        hidden.Items.ShouldBeEmpty();
        hidden.Total.ShouldBe(0);
        (await FindAsAsync(principal, "quarterly merg")).Items.ShouldBeEmpty();
        (await FindAsync("quarterly merg")).ShouldBe([article]);
    }

    // The rows of a field that is no longer searchable, or is now hidden, stay until the entry is written again; they never
    // match, not even for a caller who may read every field.
    [Fact]
    public async Task TheRowsOfAFieldNoLongerSearchableNeverMatch()
    {
        await MigrateAsync();
        var article = await CreateAsync("Quarterly report", "Merger talks", publish: true);
        await DrainAsync();
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<SearchPlugin>>();
            database.Add(new SearchEntry { DocumentId = article, Locale = "en", State = SearchStates.Published, Type = "article", SiteId = DefaultSite.Id, Field = "notes", Text = "stale", UpdatedAt = DateTimeOffset.UnixEpoch });
            await database.SaveChangesAsync(Token);
        }

        (await FindAsync("stale")).ShouldBeEmpty();
        (await FindAsync("quarterly")).ShouldBe([article]);
    }

    [Fact]
    public async Task TheRebuildWritesTheIndexAgainAndASecondRunChangesNothing()
    {
        await MigrateAsync();
        var article = await CreateAsync("Rebuilt words", "Body", publish: true);
        await DrainAsync();
        var indexed = await RowsAsync();
        indexed.Count.ShouldBe(4);

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<SearchPlugin>>();
            await database.Set<SearchEntry>().ExecuteDeleteAsync(Token);
            database.Add(new SearchEntry { DocumentId = Guid.CreateVersion7(), Locale = "en", State = SearchStates.Draft, Type = "article", SiteId = DefaultSite.Id, Text = "gone", UpdatedAt = DateTimeOffset.UnixEpoch });
            database.Add(new SearchEntry { DocumentId = Guid.CreateVersion7(), Locale = "", State = SearchStates.Draft, Type = "removed", SiteId = null, Text = "gone", UpdatedAt = DateTimeOffset.UnixEpoch });
            await database.SaveChangesAsync(Token);
        }

        (await RebuildAsync()).ShouldBe((4, 2));
        (await RowsAsync()).Select(row => (row.DocumentId, row.Locale, row.State, row.Field, row.Text)).ShouldBe(indexed.Select(row => (row.DocumentId, row.Locale, row.State, row.Field, row.Text)), ignoreOrder: true);
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

    private async Task<Guid> RoleAsync(PermissionConstraints? constraints)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        var role = new Role { Name = $"reader-{Guid.NewGuid():N}" };
        context.Add(role);
        await context.SaveChangesAsync(Token);
        context.Add(new RoleGrant { RoleId = role.Id, Permission = "content.article.read", Constraints = constraints?.ToJson() });
        await context.SaveChangesAsync(Token);
        return role.Id;
    }

    // A service account with the role in its claim, held to the role of its client when it has one.
    private static Principal Caller(Guid role, Guid? clientRole = null)
    {
        var claims = new Dictionary<string, string> { [ClaimPrincipalRoles.Claim] = role.ToString(), [TokenScopes.Claim] = TokenScopes.Management };
        if (clientRole is { } client)
        {
            claims[CmsClaimTypes.ClientRole] = client.ToString();
        }

        return new Principal(PrincipalKind.ServiceAccount, Guid.CreateVersion7(), "Reader", claims);
    }

    private async Task<PagedResult<ContentDocument>> FindAsAsync(Principal principal, string text)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>();
        accessor.Current = new OperationContext(principal, Origin.Api, new SiteId(DefaultSite.Id), "en", null, "search", DateTimeOffset.UtcNow);
        try
        {
            return await scope.ServiceProvider.GetRequiredService<IContentService>().QueryAsync("article", new ContentQuery { Search = text, Locale = "en", Page = new PageRequest { WithTotal = true } }, Token);
        }
        finally
        {
            accessor.Current = null;
        }
    }

    private async Task<IReadOnlyList<Guid>> MatchAsync(string text, IReadOnlyList<string>? fields)
    {
        using var system = SystemOperation.Begin(_host.Services.GetRequiredService<IOperationContextAccessor>(), Origin.Seed, new SiteId(DefaultSite.Id));
        var request = new SearchRequest("article", text, 100) { Locale = "en", Site = new SiteId(DefaultSite.Id), Status = QueryStatus.Published, Fields = fields };
        return await _host.Services.GetRequiredService<ISearchIndex>().SearchAsync(request, Token);
    }

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
