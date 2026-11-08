using FluentCMS.Content;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Data.Tests.Storage;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Query;

// The plan of a published lookup by a [Unique] field, through the engine, on SQLite.
public sealed class QueryIndexTests : IDisposable
{
    private static readonly SchemaModel Schema = SampleSchema.NewBuilder()
        .AddCollection("post", type => type
            .Localized()
            .Field("title", "text", field => field.Localizable())
            .Field("slug", "slug", field => field.Localizable().Unique()))
        .AddCollection("page", type => type
            .Field("title", "text")
            .Field("path", "text", field => field.Unique()))
        .AddCollection("label", type => type
            .Scope(ContentScope.Global)
            .Field("name", "text", field => field.Unique()))
        .AddCollection("note", type => type
            .Scope(ContentScope.Global)
            .Localized()
            .Field("title", "text", field => field.Localizable())
            .Field("slug", "slug", field => field.Localizable().Unique()))
        .Build();

    private readonly QueryDatabase _database = new(schema: Schema);

    public QueryIndexTests()
    {
        for (var i = 0; i < 200; i++)
        {
            AddPost($"s{i}", QueryDatabase.SiteA);
            AddPage($"/p{i}", QueryDatabase.SiteA);
            AddLabel($"l{i}");
            AddNote($"n{i}");
        }

        _database.Context.SaveChanges();
    }

    public void Dispose() => _database.Dispose();

    [SqliteFact("SQLite's query plans")]
    public void ALocalizedLookupSeeksInTheUniqueIndexWithTheSiteAndTheLocale()
    {
        Plan("post", $"filter[slug][eq]=s5&locale=en&site={QueryDatabase.SiteA}").ShouldContain("ux_cms_post_i18n_slug (site_id=? AND locale=? AND slug=?)");
    }

    [SqliteFact("SQLite's query plans")]
    public void ANonLocalizedLookupSeeksInTheUniqueIndexWithTheSite()
    {
        Plan("page", $"filter[path][eq]=/p5&site={QueryDatabase.SiteA}").ShouldContain("ux_cms_page_path (site_id=? AND path=?)");
    }

    [SqliteFact("SQLite's query plans")]
    public void AGlobalTypeSeeksInAnIndexWithoutTheSite()
    {
        Plan("label", "filter[name][eq]=l5").ShouldContain("ux_cms_label_name (name=?)");
        Plan("note", "filter[slug][eq]=n5&locale=en").ShouldContain("ux_cms_note_i18n_slug (locale=? AND slug=?)");
    }

    [SqliteFact("SQLite's query plans")]
    public void ADraftLookupDoesNotUseThePublishedIndex()
    {
        var plan = Plan("post", $"filter[slug][eq]=s5&locale=en&status=draft&site={QueryDatabase.SiteA}", new QueryAccess());

        plan.ShouldNotContain("ux_cms_post_i18n_slug");
    }

    [SqliteFact("SQLite's query plans")]
    public async Task TheLookupStillReturnsOnlyTheEntryOfTheSite()
    {
        var other = AddPost("s5", QueryDatabase.SiteB);
        _database.Context.SaveChanges();

        var ofA = await _database.RunAsync($"filter[slug][eq]=s5&locale=en&site={QueryDatabase.SiteA}", type: "post");
        var ofB = await _database.RunAsync($"filter[slug][eq]=s5&locale=en&site={QueryDatabase.SiteB}", type: "post");

        ofA.Items.Single()["id"].ShouldNotBe(other);
        ofB.Items.Single()["id"].ShouldBe(other);
    }

    private string Plan(string type, string query, IContentQueryAccess? access = null) =>
        QueryPlanTests.Plan(_database.Engine(access).Compile(type, BracketQueryParser.Parse(query)).Rows);

    private Guid AddPost(string slug, Guid site)
    {
        var id = AddDocument("post", "en", site);
        _database.Context.Rows("cms_post").Add(new Dictionary<string, object> { ["document_id"] = id, ["site_id"] = site, ["state"] = ContentState.Published });
        _database.Context.Rows("cms_post_i18n").Add(new Dictionary<string, object> { ["document_id"] = id, ["site_id"] = site, ["locale"] = "en", ["state"] = ContentState.Published, ["title"] = slug, ["slug"] = slug });
        return id;
    }

    private void AddPage(string path, Guid site)
    {
        var id = AddDocument("page", "", site);
        _database.Context.Rows("cms_page").Add(new Dictionary<string, object> { ["document_id"] = id, ["site_id"] = site, ["state"] = ContentState.Published, ["title"] = path, ["path"] = path });
    }

    private void AddLabel(string name)
    {
        var id = AddDocument("label", "", null);
        _database.Context.Rows("cms_label").Add(new Dictionary<string, object> { ["document_id"] = id, ["state"] = ContentState.Published, ["name"] = name });
    }

    private void AddNote(string slug)
    {
        var id = AddDocument("note", "en", null);
        _database.Context.Rows("cms_note").Add(new Dictionary<string, object> { ["document_id"] = id, ["state"] = ContentState.Published });
        _database.Context.Rows("cms_note_i18n").Add(new Dictionary<string, object> { ["document_id"] = id, ["locale"] = "en", ["state"] = ContentState.Published, ["title"] = slug, ["slug"] = slug });
    }

    private Guid AddDocument(string type, string locale, Guid? site)
    {
        var id = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        _database.Context.Documents.Add(new Document
        {
            DocumentId = id,
            Locale = locale,
            Type = type,
            SiteId = site,
            Status = EntryStatus.Published,
            DraftVersion = 1,
            PublishedVersion = 1,
            PublishedAt = now,
            CreatedAt = now,
            CreatedBy = Guid.Empty,
            UpdatedAt = now,
            UpdatedBy = Guid.Empty,
        });
        return id;
    }
}
