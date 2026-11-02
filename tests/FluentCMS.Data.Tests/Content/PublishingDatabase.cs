using FluentCMS.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Publishing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

// The publishing calls, each a request of its own like those of ServiceDatabase, and what they leave in the tables.
internal static class PublishingDatabase
{
    private static readonly string[] ArticleTables = ["cms_article", "cms_article_i18n"];

    public static Task<PublishResult<ContentDocument>> PublishAsync(this ServiceDatabase database, Principal principal, Guid id, string? locale = null, int? expected = null, string type = "article", string operationLocale = "en") =>
        database.AsAsync(principal, services => services.GetRequiredService<IContentService>().PublishAsync(type, id, locale, expected, Token), operationLocale);

    public static Task<ContentDocument> UnpublishAsync(this ServiceDatabase database, Principal principal, Guid id, string? locale = null, string type = "article", string operationLocale = "en") =>
        database.AsAsync(principal, services => services.GetRequiredService<IContentService>().UnpublishAsync(type, id, locale, Token), operationLocale);

    public static Task<ContentDocument> DiscardAsync(this ServiceDatabase database, Principal principal, Guid id, string? locale = null, int? expected = null, string type = "article", string operationLocale = "en") =>
        database.AsAsync(principal, services => services.GetRequiredService<IContentService>().DiscardDraftAsync(type, id, locale, expected, Token), operationLocale);

    public static Task<IReadOnlyList<VersionInfo>> VersionsAsync(this ServiceDatabase database, Principal principal, Guid id, string? locale = null, string type = "article") =>
        database.AsAsync(principal, services => services.GetRequiredService<IContentService>().GetVersionsAsync(type, id, locale, Token));

    public static Task<ContentDocument?> VersionAsync(this ServiceDatabase database, Principal principal, Guid id, int number, string? locale = null, string type = "article") =>
        database.AsAsync(principal, services => services.GetRequiredService<IContentService>().GetVersionAsync(type, id, number, locale, Token));

    public static Task<ContentDocument> RestoreVersionAsync(this ServiceDatabase database, Principal principal, Guid id, int number, string? locale = null, int? expected = null, string type = "article") =>
        database.AsAsync(principal, services => services.GetRequiredService<IContentService>().RestoreVersionAsync(type, id, number, locale, expected, Token));

    public static Task<IReadOnlyList<VersionChange>> DiffAsync(this ServiceDatabase database, Principal principal, Guid id, int from, int to, string? locale = null, string type = "article") =>
        database.AsAsync(principal, services => services.GetRequiredService<IContentService>().DiffAsync(type, id, from, to, locale, Token));

    public static Document Document(this ServiceDatabase database, Guid id, string locale = "en") =>
        database.Read(context => context.Documents.IgnoreQueryFilters().AsNoTracking().Single(document => document.DocumentId == id && document.Locale == locale));

    public static Dictionary<string, object>? PublishedRow(this ServiceDatabase database, string table, Guid id, string? locale = null) =>
        database.Read(context => context.Rows(table).AsNoTracking().SingleOrDefault(row =>
            EF.Property<Guid>(row, StorageNames.DocumentId) == id
            && EF.Property<short>(row, StorageNames.State) == ContentState.Published
            && (locale == null || EF.Property<string>(row, StorageNames.Locale) == locale)));

    public static List<Reference> References(this ServiceDatabase database, Guid id, short state) =>
        database.References(id).Where(reference => reference.State == state).ToList();

    // Everything one entry holds, so a test can say that a failed transition changed none of it.
    public static string Trace(this ServiceDatabase database, Guid id) => database.Read(context =>
    {
        var documents = context.Documents.IgnoreQueryFilters().AsNoTracking().Where(document => document.DocumentId == id).ToList()
            .OrderBy(document => document.Locale)
            .Select(document => $"{document.Locale}:{document.Status}:{document.DraftVersion}:{document.PublishedVersion}:{document.PublishedAt}:{document.UpdatedAt}:{document.UpdatedBy}");
        var versions = context.Versions.AsNoTracking().Where(version => version.DocumentId == id).ToList()
            .OrderBy(version => version.Locale).ThenBy(version => version.Number)
            .Select(version => $"{version.Locale}:{version.Number}:{version.StatusAfter}:{version.Snapshot}");
        var rows = ArticleTables.SelectMany(table => context.Rows(table).AsNoTracking()
            .Where(row => EF.Property<Guid>(row, StorageNames.DocumentId) == id).ToList()
            .Select(row => string.Join(',', row.Where(column => column.Key != StorageNames.Id).OrderBy(column => column.Key, StringComparer.Ordinal).Select(column => $"{column.Key}={column.Value}")))
            .OrderBy(row => row, StringComparer.Ordinal));
        var references = context.References.AsNoTracking().Where(reference => reference.SourceDocumentId == id).ToList()
            .Select(reference => $"{reference.State}:{reference.Locale}:{reference.Field}:{reference.Position}:{reference.TargetId}")
            .OrderBy(row => row, StringComparer.Ordinal);
        return string.Join('\n', documents.Concat(versions).Concat(rows).Concat(references));
    });
}
