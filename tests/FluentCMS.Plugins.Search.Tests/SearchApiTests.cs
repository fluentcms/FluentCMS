using System.Net;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Sites;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Search.Tests.SearchTesting;

namespace FluentCMS.Plugins.Search.Tests;

// q= on the Delivery and Management lists, the refusals of the plugin, the per-site gate, the rebuild and a deleted site.
public sealed class SearchApiTests
{
    private const string Delivery = "/api/delivery/v1/article";
    private const string Management = "/api/management/v1/content/article";

    [Fact]
    public async Task DeliveryAndManagementListsFindByAWordAndTheStartOfAWordAndADraftOnlyOnManagement()
    {
        await using var search = new SearchHost();
        var editor = await search.EditorAsync();
        var articles = editor.Content<Article>();
        var article = await articles.CreateAsync(new Article { Title = "Alpha release notes" }, Token);
        var published = await articles.PublishAsync(article.Id, cancellationToken: Token);
        await articles.UpdateAsync(article.Id, new Article { Title = "Beta release notes" }, published.Entry.Version, Token);
        await search.PumpAsync();
        using var delivery = await search.ClientAsync(ApiKeyClass.Delivery);
        using var management = await search.ClientAsync(ApiKeyClass.Management);

        (await IdsAsync(delivery, $"{Delivery}?q=alpha")).ShouldBe([article.Id]);
        (await IdsAsync(delivery, $"{Delivery}?q=release%20no")).ShouldBe([article.Id]);
        (await IdsAsync(delivery, $"{Delivery}?q=beta")).ShouldBeEmpty();
        (await IdsAsync(management, $"{Management}?q=beta&status=draft")).ShouldBe([article.Id]);
        (await IdsAsync(management, $"{Management}?q=alpha&status=draft")).ShouldBeEmpty();
        (await IdsAsync(management, $"{Management}?q=alpha&status=published")).ShouldBe([article.Id]);
    }

    [Fact]
    public async Task ATypeWithoutASearchableFieldIsRefusedWithTheCodeOfThePlugin()
    {
        await using var search = new SearchHost();
        using var delivery = await search.ClientAsync(ApiKeyClass.Delivery);

        using var response = await delivery.GetAsync(new Uri("/api/delivery/v1/note?q=anything", UriKind.Relative), Token);

        var problem = await JsonAsync(response, HttpStatusCode.BadRequest);
        problem["code"]!.GetValue<string>().ShouldBe(SearchErrorCodes.NotSearchable);
        problem["errors"]![0]!["path"]!.GetValue<string>().ShouldBe("q");
    }

    [Fact]
    public async Task ASiteWhereThePluginIsDisabledRefusesASearchAndIsRebuiltWhenItIsEnabledAgain()
    {
        await using var search = new SearchHost();
        var site = await search.SiteAsync();
        var editor = await search.EditorAsync(site);
        var articles = editor.Content<Article>();
        var article = await articles.CreateAsync(new Article { Title = "Before" }, Token);
        await search.PumpAsync();
        using var management = await search.ClientAsync(ApiKeyClass.Management, site: site);

        await search.SetEnabledAsync(site, enabled: false);
        using (var response = await management.GetAsync(new Uri($"{Management}?q=before&status=draft", UriKind.Relative), Token))
        {
            (await JsonAsync(response, HttpStatusCode.BadRequest))["code"]!.GetValue<string>().ShouldBe(SearchErrorCodes.NoSearchIndex);
        }

        // Nothing of the site reaches the handlers while the plugin is off there.
        await articles.UpdateAsync(article.Id, new Article { Title = "After" }, article.Version, Token);
        await search.PumpAsync();

        await search.SetEnabledAsync(site, enabled: true);
        await search.PumpAsync();

        (await IdsAsync(management, $"{Management}?q=after&status=draft")).ShouldBe([article.Id]);
        (await IdsAsync(management, $"{Management}?q=before&status=draft")).ShouldBeEmpty();
        search.Logs.Of(9382).ShouldHaveSingleItem().ShouldContain(site.ToString());
    }

    [Fact]
    public async Task TheRebuildIndexesWhatWasThereInBatchesAndASecondRunChangesNothing()
    {
        await using var search = new SearchHost(host => host.Seed(Enumerable.Range(0, 105).Select(index => new Article { Title = $"Entry number{index:D3}" }).ToArray()));
        var editor = await search.EditorAsync();
        var tag = await editor.Content<Tag>().CreateAsync(new Tag { Name = "Global tag" }, Token);
        await search.PumpAsync();
        var indexed = await search.RowsAsync();
        indexed.Count.ShouldBe(106);

        // What a host has before it adds the plugin, and a row of an entry that is gone.
        await search.ClearIndexAsync();
        await search.AddRowAsync(new SearchEntry { DocumentId = Guid.CreateVersion7(), Locale = "en", State = SearchStates.Draft, Type = "article", SiteId = DefaultSite.Id, Text = "gone", UpdatedAt = DateTimeOffset.UnixEpoch });
        await search.AddRowAsync(new SearchEntry { DocumentId = Guid.CreateVersion7(), Locale = "", State = SearchStates.Draft, Type = "removed-type", SiteId = null, Text = "gone", UpdatedAt = DateTimeOffset.UnixEpoch });
        using var management = await search.ClientAsync(ApiKeyClass.Management);

        using (var response = await PostAsync(management, Rebuild))
        {
            (await JsonAsync(response, HttpStatusCode.Accepted))["jobId"]!.GetValue<Guid>().ShouldNotBe(Guid.Empty);
        }

        await search.PumpAsync();

        var rebuilt = await search.RowsAsync();
        rebuilt.Select(row => (row.DocumentId, row.Locale, row.State, row.Text)).ShouldBe(indexed.Select(row => (row.DocumentId, row.Locale, row.State, row.Text)), ignoreOrder: true);
        (await FindAsync(editor, "article", "number104", status: QueryStatus.Draft)).ShouldHaveSingleItem();
        (await FindAsync(editor, "tag", "global", status: QueryStatus.Draft)).ShouldBe([tag.Id]);
        search.Logs.Of(9380).ShouldHaveSingleItem().ShouldContain("wrote 106 rows and removed 2");

        using (var again = await PostAsync(management, Rebuild, new { type = "article" }))
        {
            again.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        await search.PumpAsync();

        search.Logs.Of(9380)[^1].ShouldContain("Search rebuild of article in site");
        search.Logs.Of(9380)[^1].ShouldContain("wrote 0 rows and removed 0");
        (await search.RowsAsync()).Select(row => (row.Id, row.UpdatedAt)).ShouldBe(rebuilt.Select(row => (row.Id, row.UpdatedAt)));
    }

    [Fact]
    public async Task TheRebuildIndexesEveryLocaleAndTheDraftsInTheTrash()
    {
        await using var search = new SearchHost();
        await search.AddLocaleAsync(DefaultSite.Id, "fr");
        var editor = await search.EditorAsync();
        var english = editor.Content<Article>();
        var french = editor.In("fr").Content<Article>();
        var both = await english.CreateAsync(new Article { Title = "Hello" }, Token);
        await french.UpdateAsync(both.Id, new Article { Title = "Bonjour" }, 0, Token);
        var onlyFrench = await french.CreateAsync(new Article { Title = "Seulement" }, Token);
        var trashed = await english.CreateAsync(new Article { Title = "Discarded" }, Token);
        await english.DeleteAsync(trashed.Id, Token);
        await search.PumpAsync();
        var indexed = await search.RowsAsync();
        await search.ClearIndexAsync();
        using var management = await search.ClientAsync(ApiKeyClass.Management);

        using (var response = await PostAsync(management, Rebuild))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        await search.PumpAsync();

        (await search.RowsAsync()).Select(row => (row.DocumentId, row.Locale, row.State, row.Text)).ShouldBe(indexed.Select(row => (row.DocumentId, row.Locale, row.State, row.Text)), ignoreOrder: true);
        (await FindAsync(editor, "article", "seulement", "fr", QueryStatus.Draft)).ShouldBe([onlyFrench.Id]);
        indexed.ShouldContain(row => row.DocumentId == trashed.Id && row.State == SearchStates.Draft);
    }

    [Fact]
    public async Task TheRebuildNeedsSearchManageAndATypeWithASearchableField()
    {
        await using var search = new SearchHost();
        await search.Host.CreateRoleAsync("nothing", [], Token);
        await search.Host.CreateRoleAsync("indexer", [SearchPermissions.Manage], Token);
        using var nobody = await search.ClientAsync(ApiKeyClass.Management, "nothing");
        using var indexer = await search.ClientAsync(ApiKeyClass.Management, "indexer");

        using (var response = await PostAsync(nobody, Rebuild))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using (var response = await PostAsync(indexer, Rebuild, new { type = "missing" }))
        {
            (await JsonAsync(response, HttpStatusCode.UnprocessableEntity))["code"]!.GetValue<string>().ShouldBe(SearchErrorCodes.InvalidRebuild);
        }

        using (var response = await PostAsync(indexer, Rebuild, new { type = "note" }))
        {
            (await JsonAsync(response, HttpStatusCode.BadRequest))["code"]!.GetValue<string>().ShouldBe(SearchErrorCodes.NotSearchable);
        }

        using (var response = await PostAsync(indexer, Rebuild, new { type = "article" }))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }
    }

    // An import raises the events of any write, with the import origin, so what it writes is indexed as it commits.
    [Fact]
    public async Task AnImportIsIndexed()
    {
        await using var source = new SearchHost();
        var articles = (await source.EditorAsync()).Content<Article>();
        var article = await articles.CreateAsync(new Article { Title = "Imported words" }, Token);
        await articles.PublishAsync(article.Id, cancellationToken: Token);
        using var sourceAdmin = await source.ClientAsync(ApiKeyClass.Management);
        using var exported = await PostAsync(sourceAdmin, "/api/management/v1/transfer/export", new { });
        exported.StatusCode.ShouldBe(HttpStatusCode.OK, await exported.Content.ReadAsStringAsync(Token));
        var package = await exported.Content.ReadAsByteArrayAsync(Token);

        await using var target = new SearchHost();
        using var targetAdmin = await target.ClientAsync(ApiKeyClass.Management);
        using var body = new ByteArrayContent(package);
        body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
        using var imported = await targetAdmin.PostAsync(new Uri("/api/management/v1/transfer/import", UriKind.Relative), body, Token);
        imported.StatusCode.ShouldBe(HttpStatusCode.OK, await imported.Content.ReadAsStringAsync(Token));
        await target.PumpAsync();

        (await IdsAsync(targetAdmin, $"{Management}?q=imported&status=published")).ShouldBe([article.Id]);
    }

    [Fact]
    public async Task TheRowsOfADeletedSiteGo()
    {
        await using var search = new SearchHost();
        var site = await search.SiteAsync();
        await search.AddRowAsync(new SearchEntry { DocumentId = Guid.CreateVersion7(), Locale = "en", State = SearchStates.Published, Type = "article", SiteId = site, Text = "left", UpdatedAt = DateTimeOffset.UnixEpoch });
        await search.AddRowAsync(new SearchEntry { DocumentId = Guid.CreateVersion7(), Locale = "en", State = SearchStates.Published, Type = "article", SiteId = DefaultSite.Id, Text = "kept", UpdatedAt = DateTimeOffset.UnixEpoch });

        await using (var scope = search.Services.CreateAsyncScope())
        {
            using var system = SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), Origin.Seed, new SiteId(DefaultSite.Id));
            await scope.ServiceProvider.GetRequiredService<ISiteAdministration>().DeleteAsync(new SiteId(site), Token);
        }

        await search.PumpAsync();

        (await search.RowsAsync()).ShouldHaveSingleItem().Text.ShouldBe("kept");
        search.Logs.Of(9381).ShouldHaveSingleItem().ShouldContain(site.ToString());
    }

    private static async Task<List<Guid>> IdsAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path + "&locale=en", UriKind.Relative), Token);
        var list = await JsonAsync(response, HttpStatusCode.OK);
        return [.. list["items"]!.AsArray().Select(item => Guid.Parse(item!["id"]!.GetValue<string>()))];
    }
}
