using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Data.Tests.Authorization;
using FluentCMS.Data.Tests.Query;
using FluentCMS.Schema;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluentCMS.Data.Tests.Content;

// The real service on a real SQLite file, with sites A and B that list en, fr and fr-CA. Every call is a request of its
// own: a new scope, so a new DbContext, under the operation the entry point would set.
internal sealed class ServiceDatabase : IDisposable
{
    public static readonly SiteId SiteA = new(QueryDatabase.SiteA);
    public static readonly SiteId SiteB = new(QueryDatabase.SiteB);

    // The sample types in the shape a host writes them, with a unique field in each table, for the tests of the document service.
    public static readonly SchemaModel Schema = SampleSchema.NewBuilder()
        .AddComponent("seo", component => component
            .Field("metaTitle", "text", field => field.Length(null, 10))
            .Field("image", "media")
            .Field("internal", "text", field => field.Hidden()))
        .AddBlock("hero", block => block
            .Field("heading", "text", field => field.Length(null, 10))
            .Field("on", "date")
            .Field("at", "dateTime")
            .Field("note", "richText")
            .Field("image", "media")
            .Field("cover", "relation", field => field.Relation("author"))
            .Field("internal", "text", field => field.Hidden())
            .Field("details", "component", field => field.Setting(FieldSettings.Component, "seo")))
        .AddBlock("quote", block => block.Field("text", "text"))
        .AddBlock("other", block => block.Field("text", "text"))
        .AddCollection("author", type => type
            .Field("name", "text", field => field.Required())
            .Field("secret", "text", field => field.Hidden()))
        .AddCollection("tag", type => type
            .Scope(ContentScope.Global)
            .Field("name", "text")
            .Field("code", "text", field => field.Unique()))
        .AddCollection("article", type => type
            .Localized()
            .Field("title", "text", field => field.Required().Localizable().Length(null, 40))
            .Field("slug", "slug", field => field.Localizable().Unique().Setting(FieldSettings.Source, "title"))
            .Field("subtitle", "text", field => field.Localizable())
            .Field("body", "richText", field => field.Localizable())
            .Field("sponsor", "relation", field => field.Localizable().Relation("author"))
            .Field("views", "integer", field => field.Range(0, 1000))
            .Field("code", "text", field => field.Unique())
            .Field("region", "text")
            .Field("category", "select", field => field.DefaultValue("news").Setting(FieldSettings.Options, new[] { "news", "opinion" }))
            .Field("labels", "select", field => field.Multiple().Setting(FieldSettings.Options, new[] { "net", "sql" }))
            .Field("publishedOn", "dateTime")
            .Field("issued", "date")
            .Field("price", "decimal")
            .Field("author", "relation", field => field.Relation("author"))
            .Field("tags", "relation", field => field.Multiple().Relation("tag", ordered: true))
            .Field("hero", "media")
            .Field("sections", "blocks", field => field.Setting(FieldSettings.Blocks, new[] { "hero", "quote" }))
            .Field("seo", "component", field => field.Setting(FieldSettings.Component, "seo"))
            .Field("faq", "component", field => field.Multiple().Setting(FieldSettings.Component, "seo"))
            .Field("metadata", "json")
            .Field("stamp", "text", field => field.IsReadOnly())
            .Field("secret", "text", field => field.Hidden()))
        .AddSingleton("settings", type => type.Field("name", "text"))
        .Build();

    public ServiceDatabase(Action<FluentCmsBuilder>? configure = null, SchemaModel? schema = null)
    {
        Database = new QueryDatabase(configure, schema ?? Schema);

        var context = Database.Context;
        foreach (var (site, slug) in new[] { (QueryDatabase.SiteA, "a"), (QueryDatabase.SiteB, "b") })
        {
            context.Add(new Site { Id = site, Name = slug, Slug = slug, DefaultLocale = "en", CreatedAt = DateTimeOffset.UtcNow });
            context.AddRange(
                new SiteLocale { SiteId = site, Code = "en", Position = 0, Enabled = true },
                new SiteLocale { SiteId = site, Code = "fr", Position = 1, Enabled = true },
                new SiteLocale { SiteId = site, Code = "fr-CA", Position = 2, Enabled = true });
        }

        context.SaveChanges();
    }

    public QueryDatabase Database { get; }

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    public static Principal Editor { get; } = AuthorizationDatabase.User([BuiltInRoles.Editor], [TokenScopes.Management]);

    public static Principal AuthorRole(Guid? id = null) => AuthorizationDatabase.User([BuiltInRoles.Author], [TokenScopes.Management], id);

    public static Principal WithRole(Guid role, Guid? id = null) => AuthorizationDatabase.User([role], [TokenScopes.Management], id);

    public async Task<T> AsAsync<T>(Principal principal, Func<IServiceProvider, Task<T>> work, string locale = "en", SiteId? site = null, Origin origin = Origin.Api)
    {
        using var scope = Database.NewScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>();
        accessor.Current = new OperationContext(principal, origin, site ?? SiteA, locale, "client", "correlation", DateTimeOffset.UtcNow);
        try
        {
            return await work(scope.ServiceProvider);
        }
        finally
        {
            accessor.Current = null;
        }
    }

    public Task<ContentDocument> CreateAsync(Principal principal, ContentDocument entry, string type = "article", string locale = "en", SiteId? site = null) =>
        AsAsync(principal, services => services.GetRequiredService<IContentService>().CreateAsync(type, entry, Token), locale, site);

    public Task<ContentDocument> UpdateAsync(Principal principal, Guid id, ContentDocument entry, int version, string type = "article", string locale = "en", SiteId? site = null) =>
        AsAsync(principal, services => services.GetRequiredService<IContentService>().UpdateAsync(type, id, entry, version, Token), locale, site);

    public Task<ContentDocument> PatchAsync(Principal principal, Guid id, ContentDocument patch, int version, string type = "article", string locale = "en") =>
        AsAsync(principal, services => services.GetRequiredService<IContentService>().PatchAsync(type, id, patch, version, Token), locale);

    public Task<ContentDocument?> GetAsync(Principal principal, Guid id, GetOptions? options = null, string type = "article", string locale = "en", SiteId? site = null) =>
        AsAsync(principal, services => services.GetRequiredService<IContentService>().GetAsync(type, id, options ?? new GetOptions { Status = QueryStatus.Draft }, Token), locale, site);

    public Task<PagedResult<ContentDocument>> QueryAsync(Principal principal, ContentQuery? query = null, string type = "article", string locale = "en", SiteId? site = null) =>
        AsAsync(principal, services => services.GetRequiredService<IContentService>().QueryAsync(type, query ?? new ContentQuery { Status = QueryStatus.Draft }, Token), locale, site);

    public Task DeleteAsync(Principal principal, Guid id, string type = "article", string locale = "en") =>
        AsAsync<object?>(principal, async services =>
        {
            await services.GetRequiredService<IContentService>().DeleteAsync(type, id, Token);
            return null;
        }, locale);

    public Task<ContentDocument> RestoreAsync(Principal principal, Guid id, string type = "article", string locale = "en") =>
        AsAsync(principal, services => services.GetRequiredService<IContentService>().RestoreAsync(type, id, Token), locale);

    public static Guid IdOf(ContentDocument entry) => (Guid)entry["id"]!;

    public static int VersionOf(ContentDocument entry) => (int)entry["version"]!;

    public T Read<T>(Func<CmsDbContext, T> read)
    {
        using var scope = Database.NewScope();
        return read(scope.ServiceProvider.GetRequiredService<CmsDbContext>());
    }

    public List<ContentVersion> Versions(Guid id) =>
        Read(context => context.Versions.AsNoTracking().Where(version => version.DocumentId == id).OrderBy(version => version.Locale).ThenBy(version => version.Number).ToList());

    public List<Reference> References(Guid id) =>
        Read(context => context.References.AsNoTracking().Where(reference => reference.SourceDocumentId == id).OrderBy(reference => reference.Field).ThenBy(reference => reference.Position).ToList());

    public Dictionary<string, object> Row(string table, Guid id, string? locale = null) =>
        Read(context => context.Rows(table).AsNoTracking().Single(row =>
            EF.Property<Guid>(row, StorageNames.DocumentId) == id
            && EF.Property<short>(row, StorageNames.State) == ContentState.Draft
            && (locale == null || EF.Property<string>(row, StorageNames.Locale) == locale)));

    // What each table holds for all content, so that a test can say that a failed operation left no trace in any of them.
    public string Footprint() => Read(context => string.Join(
        ',',
        context.Documents.IgnoreQueryFilters().Count(),
        context.Rows("cms_article").Count(),
        context.Rows("cms_article_i18n").Count(),
        context.References.Count(),
        context.Versions.Count()));

    public void Dispose() => Database.Dispose();
}
