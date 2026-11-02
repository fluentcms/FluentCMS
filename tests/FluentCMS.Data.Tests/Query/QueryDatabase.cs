using FluentCMS.Content;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Data.Tests.Sqlite;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluentCMS.Data.Tests.Query;

// A database of the real provider with a schema that has every kind of field, and helpers that write rows the way
// the content service will.
internal sealed class QueryDatabase : IDisposable
{
    public static readonly Guid SiteA = Guid.CreateVersion7();
    public static readonly Guid SiteB = Guid.CreateVersion7();

    public static readonly SchemaModel Schema = SampleSchema.NewBuilder()
        .AddComponent("seo", component => component
            .Field("metaTitle", "text")
            .Field("metaDescription", "text")
            .Field("internal", "text", field => field.Hidden()))
        .AddBlock("hero", block => block
            .Field("heading", "text")
            .Field("level", "integer")
            .Field("score", "decimal")
            .Field("featured", "boolean")
            .Field("on", "date")
            .Field("kinds", "select", field => field.Multiple().Setting(FieldSettings.Options, new[] { "wide", "tall" }))
            .Field("note", "richText")
            .Field("internal", "text", field => field.Hidden()))
        .AddBlock("quote", block => block.Field("text", "text"))
        .AddCollection("company", type => type
            .Field("name", "text")
            .Field("owner", "relation", field => field.Relation("author")))
        .AddCollection("author", type => type
            .Field("name", "text", field => field.Required())
            .Field("secret", "text", field => field.Hidden())
            .Field("company", "relation", field => field.Relation("company")))
        .AddCollection("tag", type => type
            .Scope(ContentScope.Global)
            .Field("name", "text"))
        .AddCollection("article", type => type
            .Localized()
            .Field("title", "text", field => field.Required().Localizable())
            .Field("slug", "slug", field => field.Localizable())
            .Field("subtitle", "text", field => field.Localizable())
            .Field("body", "richText", field => field.Localizable())
            .Field("views", "integer")
            .Field("price", "decimal")
            .Field("featured", "boolean")
            .Field("publishedOn", "dateTime", field => field.Indexed())
            .Field("issued", "date")
            .Field("category", "select", field => field.Setting(FieldSettings.Options, new[] { "news", "opinion" }))
            .Field("labels", "select", field => field.Multiple().Setting(FieldSettings.Options, new[] { "net", "sql", "web" }))
            .Field("contact", "email")
            .Field("link", "url")
            .Field("summary", "longText")
            .Field("author", "relation", field => field.Relation("author"))
            .Field("tags", "relation", field => field.Multiple().Relation("tag", ordered: true))
            .Field("related", "relation", field => field.Multiple().Relation("article"))
            .Field("sponsor", "relation", field => field.Localizable().Relation("author"))
            .Field("hero", "media")
            .Field("sections", "blocks", field => field.Setting(FieldSettings.Blocks, new[] { "hero", "quote" }))
            .Field("seo", "component", field => field.Setting(FieldSettings.Component, "seo"))
            .Field("faq", "component", field => field.Multiple().Setting(FieldSettings.Component, "seo"))
            .Field("metadata", "json")
            .Field("secret", "text", field => field.Hidden()))
        .Build();

    private readonly SqliteFileDatabase _database = new();
    private readonly ServiceProvider _services;
    private readonly IServiceScope _scope;

    public QueryDatabase()
    {
        _services = _database.CreateServices(Schema);
        _scope = _services.CreateScope();
        Context = _scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        Context.Database.EnsureCreated();
    }

    public CmsDbContext Context { get; }

    public IServiceProvider Services => _scope.ServiceProvider;

    public ContentQueryEngine Engine(IContentQueryAccess? access = null, ContentQueryOptions? options = null) => new(
        Context,
        Schema,
        Services.GetRequiredService<IJsonSqlDialect>(),
        access ?? new OpenContentQueryAccess(),
        options ?? new ContentQueryOptions());

    public Guid AddArticle(string title, Dictionary<string, object?>? values = null, string locale = "en", short state = ContentState.Published, Guid? id = null, Guid? site = null)
    {
        var documentId = id ?? Guid.CreateVersion7();
        var shared = new[] { "views", "price", "featured", "published_on", "issued", "category", "labels", "contact", "link", "summary", "sections", "seo_meta_title", "seo_meta_description", "seo_internal", "metadata", "secret", "faq" };

        if (id is null)
        {
            AddDocument("article", documentId, locale, state, site ?? SiteA);
            var row = new Dictionary<string, object>
            {
                ["document_id"] = documentId,
                ["site_id"] = site ?? SiteA,
                ["state"] = state,
                ["labels"] = "[]",
                ["sections"] = "[]",
                ["faq"] = "[]",
            };
            CopyValues(values, row, shared);
            Context.Rows("cms_article").Add(row);
        }
        else
        {
            AddDocument("article", documentId, locale, state, site ?? SiteA);
        }

        var localized = new Dictionary<string, object>
        {
            ["document_id"] = documentId,
            ["site_id"] = site ?? SiteA,
            ["locale"] = locale,
            ["state"] = state,
            ["title"] = title,
            ["slug"] = title.ToLowerInvariant(),
            ["body"] = RichText.Empty.Json,
        };
        CopyValues(values, localized, ["subtitle", "body"]);
        Context.Rows("cms_article_i18n").Add(localized);

        Context.SaveChanges();
        return documentId;
    }

    public Guid AddAuthor(string name, string? secret = null, short state = ContentState.Published)
    {
        var id = Guid.CreateVersion7();
        AddDocument("author", id, "", state, SiteA);
        var row = new Dictionary<string, object> { ["document_id"] = id, ["site_id"] = SiteA, ["state"] = state, ["name"] = name };
        if (secret is not null)
        {
            row["secret"] = secret;
        }

        Context.Rows("cms_author").Add(row);
        Context.SaveChanges();
        return id;
    }

    public Guid AddCompany(string name)
    {
        var id = Guid.CreateVersion7();
        AddDocument("company", id, "", ContentState.Published, SiteA);
        Context.Rows("cms_company").Add(new Dictionary<string, object> { ["document_id"] = id, ["site_id"] = SiteA, ["state"] = ContentState.Published, ["name"] = name });
        Context.SaveChanges();
        return id;
    }

    public Guid AddTag(string name)
    {
        var id = Guid.CreateVersion7();
        AddDocument("tag", id, "", ContentState.Published, null);
        Context.Rows("cms_tag").Add(new Dictionary<string, object> { ["document_id"] = id, ["state"] = ContentState.Published, ["name"] = name });
        Context.SaveChanges();
        return id;
    }

    public void Relate(string sourceType, Guid source, string field, string targetType, Guid target, int position = 0, short state = ContentState.Published, string locale = "")
    {
        Context.References.Add(new Reference
        {
            SourceType = sourceType,
            SourceDocumentId = source,
            State = state,
            Locale = locale,
            Field = field,
            Position = position,
            TargetType = targetType,
            TargetId = target,
        });
        Context.SaveChanges();
    }

    public void Trash(Guid id)
    {
        foreach (var document in Context.Documents.Where(document => document.DocumentId == id).ToList())
        {
            document.DeletedAt = DateTimeOffset.UtcNow;
        }

        Context.SaveChanges();
    }

    public void Delete(Guid id)
    {
        Context.Documents.Where(document => document.DocumentId == id).ExecuteDelete();
        Context.Rows("cms_article").Where(row => EF.Property<Guid>(row, "document_id") == id).ExecuteDelete();
        Context.Rows("cms_article_i18n").Where(row => EF.Property<Guid>(row, "document_id") == id).ExecuteDelete();
    }

    public Task<ContentQueryResult> RunAsync(string queryString, IContentQueryAccess? access = null, ContentQueryOptions? options = null, string type = "article") =>
        Engine(access, options).ExecuteAsync(type, BracketQueryParser.Parse(queryString), TestContext.Current.CancellationToken);

    public void Dispose()
    {
        _scope.Dispose();
        _services.Dispose();
        _database.Dispose();
    }

    private void AddDocument(string type, Guid id, string locale, short state, Guid? site)
    {
        var now = DateTimeOffset.UtcNow;
        Context.Documents.Add(new Document
        {
            DocumentId = id,
            Locale = locale,
            Type = type,
            SiteId = site,
            Status = state == ContentState.Published ? EntryStatus.Published : EntryStatus.Draft,
            DraftVersion = 3,
            PublishedVersion = state == ContentState.Published ? 2 : null,
            PublishedAt = state == ContentState.Published ? now : null,
            CreatedAt = now,
            CreatedBy = Guid.Empty,
            UpdatedAt = now,
            UpdatedBy = Guid.Empty,
        });
    }

    private static void CopyValues(Dictionary<string, object?>? values, Dictionary<string, object> row, string[] columns)
    {
        foreach (var column in columns)
        {
            if (values is not null && values.TryGetValue(column, out var value))
            {
                row[column] = value!;
            }
        }
    }
}

internal sealed class QueryAccess : IContentQueryAccess
{
    public HashSet<string> DeniedTypes { get; } = [];

    public HashSet<string> DeniedFields { get; } = [];

    public Dictionary<string, ContentFilter> RowRules { get; } = [];

    public bool CanReadType(SchemaType type) => !DeniedTypes.Contains(type.Name);

    public bool CanReadField(SchemaType type, string field) => !DeniedFields.Contains($"{type.Name}.{field}");

    public ContentFilter? RowRule(SchemaType type) => RowRules.GetValueOrDefault(type.Name);
}
