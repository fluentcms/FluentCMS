using FluentCMS.Content;
using FluentCMS.Core.Sites;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Search.Tests.SearchTesting;

namespace FluentCMS.Plugins.Search.Tests;

// The index as the content service reads it: each test writes entries, delivers their events and lists with a search.
public sealed class SearchIndexTests
{
    [Fact]
    public async Task AnEntryIsFoundByAWordAndByTheStartOfItsLastWord()
    {
        await using var search = new SearchHost();
        var editor = await search.EditorAsync();
        var articles = editor.Content<Article>();
        var dotnet = await articles.CreateAsync(new Article { Title = "Learning dotnet search", Body = Paragraphs("Ku", "bernetes") }, Token);
        var other = await articles.CreateAsync(new Article { Title = "Gardening" }, Token);
        await articles.PublishAsync(dotnet.Id, cancellationToken: Token);
        await articles.PublishAsync(other.Id, cancellationToken: Token);
        await search.PumpAsync();

        (await FindAsync(editor, "article", "dotnet")).ShouldBe([dotnet.Id]);
        (await FindAsync(editor, "article", "DOT")).ShouldBe([dotnet.Id]);
        (await FindAsync(editor, "article", "learning dot")).ShouldBe([dotnet.Id]);
        (await FindAsync(editor, "article", "séarch")).ShouldBe([dotnet.Id]);
        (await FindAsync(editor, "article", "bernetes")).ShouldBe([dotnet.Id]);

        // Every word matches, and only the last as the start of a word.
        (await FindAsync(editor, "article", "dot learning")).ShouldBeEmpty();
        (await FindAsync(editor, "article", "dotnet gardening")).ShouldBeEmpty();
        (await FindAsync(editor, "article", "otnet")).ShouldBeEmpty();
        (await FindAsync(editor, "article", "?!")).ShouldBeEmpty();
    }

    [Fact]
    public async Task AnEntryIsFoundOnlyOnceItsEventIsDelivered()
    {
        await using var search = new SearchHost();
        var editor = await search.EditorAsync();
        var article = await editor.Content<Article>().CreateAsync(new Article { Title = "Pending" }, Token);

        (await FindAsync(editor, "article", "pending", status: QueryStatus.Draft)).ShouldBeEmpty();
        await search.PumpAsync();
        (await FindAsync(editor, "article", "pending", status: QueryStatus.Draft)).ShouldBe([article.Id]);
    }

    [Fact]
    public async Task AChangedDraftIsFoundWithStatusDraftAndNotPublishedUntilItIsPublished()
    {
        await using var search = new SearchHost();
        var editor = await search.EditorAsync();
        var articles = editor.Content<Article>();
        var article = await articles.CreateAsync(new Article { Title = "First words" }, Token);
        await search.PumpAsync();

        (await FindAsync(editor, "article", "first", status: QueryStatus.Draft)).ShouldBe([article.Id]);
        (await FindAsync(editor, "article", "first")).ShouldBeEmpty();

        var published = await articles.PublishAsync(article.Id, cancellationToken: Token);
        var changed = await articles.UpdateAsync(article.Id, new Article { Title = "Second words" }, published.Entry.Version, Token);
        await search.PumpAsync();

        (await FindAsync(editor, "article", "second", status: QueryStatus.Draft)).ShouldBe([article.Id]);
        (await FindAsync(editor, "article", "second")).ShouldBeEmpty();
        (await FindAsync(editor, "article", "first")).ShouldBe([article.Id]);

        await articles.PublishAsync(article.Id, cancellationToken: Token);
        await search.PumpAsync();

        (await FindAsync(editor, "article", "second")).ShouldBe([article.Id]);
        (await FindAsync(editor, "article", "first")).ShouldBeEmpty();
        changed.Version.ShouldBeGreaterThan(published.Entry.Version);
    }

    [Fact]
    public async Task AnEntryIsNoLongerFoundOnceUnpublishedDeletedOrPurgedAndIsFoundAgainOnceRestored()
    {
        await using var search = new SearchHost();
        var editor = await search.EditorAsync();
        var articles = editor.Content<Article>();
        var article = await articles.CreateAsync(new Article { Title = "Ephemeral" }, Token);
        await articles.PublishAsync(article.Id, cancellationToken: Token);
        await search.PumpAsync();
        (await FindAsync(editor, "article", "ephemeral")).ShouldBe([article.Id]);

        await articles.UnpublishAsync(article.Id, cancellationToken: Token);
        await search.PumpAsync();
        (await FindAsync(editor, "article", "ephemeral")).ShouldBeEmpty();
        (await FindAsync(editor, "article", "ephemeral", status: QueryStatus.Draft)).ShouldBe([article.Id]);

        await articles.DeleteAsync(article.Id, Token);
        await search.PumpAsync();
        (await FindAsync(editor, "article", "ephemeral", status: QueryStatus.Draft)).ShouldBeEmpty();
        (await search.RowsAsync()).ShouldBeEmpty();

        // A deleted entry has no rows, so a search of the trash finds nothing.
        var trash = await editor.Content().QueryTrashAsync("article", new ContentQuery { Search = "ephemeral", Locale = "en" }, Token);
        trash.Items.ShouldBeEmpty();

        await articles.RestoreAsync(article.Id, Token);
        await search.PumpAsync();
        (await FindAsync(editor, "article", "ephemeral", status: QueryStatus.Draft)).ShouldBe([article.Id]);

        await articles.DeleteAsync(article.Id, Token);
        await articles.PurgeAsync(article.Id, Token);
        await search.PumpAsync();
        (await search.RowsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task RichTextIsSearchedAsPlainTextAndAHiddenOrUnmarkedFieldIsNotSearched()
    {
        await using var search = new SearchHost();
        var editor = await search.EditorAsync();
        var article = await editor.Content<Article>().CreateAsync(
            new Article { Title = "Title", Body = Paragraphs("The body speaks of kubernetes"), Notes = "invisible", Secret = "classified" },
            Token);
        await search.PumpAsync();

        (await FindAsync(editor, "article", "kubernetes", status: QueryStatus.Draft)).ShouldBe([article.Id]);
        (await FindAsync(editor, "article", "invisible", status: QueryStatus.Draft)).ShouldBeEmpty();
        (await FindAsync(editor, "article", "classified", status: QueryStatus.Draft)).ShouldBeEmpty();
        (await search.RowsAsync()).ShouldHaveSingleItem().Text.ShouldBe("title the body speaks of kubernetes");
    }

    [Fact]
    public async Task EachLocaleIsSearchedApartAndAFieldTheyShareIsIndexedInEach()
    {
        await using var search = new SearchHost();
        await search.AddLocaleAsync(DefaultSite.Id, "fr");
        var editor = await search.EditorAsync();
        var english = editor.Content<Article>();
        var french = editor.In("fr").Content<Article>();
        var article = await english.CreateAsync(new Article { Title = "Hello world", Tagline = "shared" }, Token);
        await french.UpdateAsync(article.Id, new Article { Title = "Bonjour monde", Tagline = "shared" }, 0, Token);
        await search.PumpAsync();

        (await FindAsync(editor, "article", "bonjour", "fr", QueryStatus.Draft)).ShouldBe([article.Id]);
        (await FindAsync(editor, "article", "bonjour", "en", QueryStatus.Draft)).ShouldBeEmpty();
        (await FindAsync(editor, "article", "hello", "en", QueryStatus.Draft)).ShouldBe([article.Id]);

        var current = await english.GetAsync(article.Id, new GetOptions { Status = QueryStatus.Draft }, Token);
        await english.UpdateAsync(article.Id, new Article { Title = "Hello world", Tagline = "changed" }, current!.Version, Token);
        await search.PumpAsync();

        (await FindAsync(editor, "article", "changed", "fr", QueryStatus.Draft)).ShouldBe([article.Id]);
        (await FindAsync(editor, "article", "shared", "fr", QueryStatus.Draft)).ShouldBeEmpty();
    }

    [Fact]
    public async Task TheSiteAndTheFilterOfTheQueryStillDecideWhatIsRead()
    {
        await using var search = new SearchHost();
        var other = await search.SiteAsync();
        var editor = await search.EditorAsync();
        var elsewhere = await search.EditorAsync(other);
        var popular = await editor.Content<Article>().CreateAsync(new Article { Title = "Shared word", Views = 10 }, Token);
        await editor.Content<Article>().CreateAsync(new Article { Title = "Shared word", Views = 1 }, Token);
        var abroad = await elsewhere.Content<Article>().CreateAsync(new Article { Title = "Shared word" }, Token);
        await search.PumpAsync();

        (await FindAsync(editor, "article", "shared", status: QueryStatus.Draft)).Count.ShouldBe(2);
        (await FindAsync(editor, "article", "shared", status: QueryStatus.Draft, filter: new FieldFilter("views", FilterOperator.GreaterThan, 5L))).ShouldBe([popular.Id]);
        (await FindAsync(elsewhere, "article", "shared", status: QueryStatus.Draft)).ShouldBe([abroad.Id]);
    }

    // The index finds both entries; the row rule of the reader, which the engine adds to the narrowed query, keeps one.
    [Fact]
    public async Task AReaderUnderARowRuleFindsOnlyTheEntriesTheRuleLetsItRead()
    {
        await using var search = new SearchHost();
        var editor = await search.EditorAsync();
        var articles = editor.Content<Article>();
        var popular = await articles.CreateAsync(new Article { Title = "Ruled word", Views = 10 }, Token);
        var quiet = await articles.CreateAsync(new Article { Title = "Ruled word", Views = 1 }, Token);
        await articles.PublishAsync(popular.Id, cancellationToken: Token);
        await articles.PublishAsync(quiet.Id, cancellationToken: Token);
        await search.PumpAsync();

        (await FindAsync(editor, "article", "ruled")).ShouldBe([popular.Id, quiet.Id], ignoreOrder: true);
        (await FindAsync(await search.RuledReaderAsync(), "article", "ruled")).ShouldBe([popular.Id]);
    }

    [Fact]
    public async Task AGlobalTypeIsFoundFromEverySite()
    {
        await using var search = new SearchHost();
        var other = await search.SiteAsync();
        var editor = await search.EditorAsync();
        var tag = await editor.Content<Tag>().CreateAsync(new Tag { Name = "Observability" }, Token);
        await search.PumpAsync();

        (await FindAsync(editor, "tag", "observ", status: QueryStatus.Draft)).ShouldBe([tag.Id]);
        (await FindAsync(await search.EditorAsync(other), "tag", "observ", status: QueryStatus.Draft)).ShouldBe([tag.Id]);
        (await search.RowsAsync()).ShouldHaveSingleItem().SiteId.ShouldBeNull();
    }

    [Fact]
    public async Task ATypeWithoutASearchableFieldRefusesASearch()
    {
        await using var search = new SearchHost();
        var editor = await search.EditorAsync();

        var exception = await Should.ThrowAsync<ValidationException>(() => FindAsync(editor, "note", "anything"));

        var error = exception.Errors.ShouldHaveSingleItem();
        error.Path.ShouldBe("q");
        error.Code.ShouldBe(SearchErrorCodes.NotSearchable);
        error.Message.ShouldContain("[Searchable]");
    }

    [Fact]
    public async Task ASeedIsIndexedAsAnyWriteIs()
    {
        await using var search = new SearchHost(host => host.Seed(new Article { Title = "Seeded entry" }));
        var editor = await search.EditorAsync();
        await search.PumpAsync();

        (await FindAsync(editor, "article", "seeded", status: QueryStatus.Draft)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task MoreMatchesThanTheLimitGiveTheMostRecentlyIndexed()
    {
        await using var search = new SearchHost();
        var editor = await search.EditorAsync();
        var first = await editor.Content<Article>().CreateAsync(new Article { Title = "Common" }, Token);
        await search.PumpAsync();
        search.Host.Time.Advance(TimeSpan.FromMinutes(1));
        var second = await editor.Content<Article>().CreateAsync(new Article { Title = "Common" }, Token);
        await search.PumpAsync();

        var index = (SearchIndex)search.Services.GetRequiredService<Providers.ISearchIndex>();
        var found = await index.SearchAsync(new Providers.SearchRequest("article", "common", 1) { Locale = "en", Status = QueryStatus.Draft, Site = new Sites.SiteId(DefaultSite.Id) }, Token);

        found.ShouldBe([second.Id]);
        first.Id.ShouldNotBe(second.Id);
    }
}
