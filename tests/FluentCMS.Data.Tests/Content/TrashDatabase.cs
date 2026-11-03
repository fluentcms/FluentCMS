using FluentCMS.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

// The trash calls, each a request of its own like those of ServiceDatabase, and what an entry leaves in the tables.
internal static class TrashDatabase
{
    public const string Nothing = "0,0,0,0,0";

    public static Task<PagedResult<ContentDocument>> TrashAsync(this ServiceDatabase database, Principal principal, ContentQuery? query = null, string type = "article", string locale = "en", SiteId? site = null) =>
        database.AsAsync(principal, services => services.GetRequiredService<IContentService>().QueryTrashAsync(type, query ?? new ContentQuery(), Token), locale, site);

    public static Task PurgeAsync(this ServiceDatabase database, Principal principal, Guid id, string type = "article", string locale = "en", SiteId? site = null) =>
        database.AsAsync<object?>(
            principal,
            async services =>
            {
                await services.GetRequiredService<IContentService>().PurgeAsync(type, id, Token);
                return null;
            },
            locale,
            site);

    public static Task DeleteInAsync(this ServiceDatabase database, Principal principal, Guid id, SiteId site, string type = "article") =>
        database.AsAsync<object?>(
            principal,
            async services =>
            {
                await services.GetRequiredService<IContentService>().DeleteAsync(type, id, Token);
                return null;
            },
            site: site);

    // Moves the moment an entry went to the trash, so that a test need not wait for a retention period to pass.
    public static void TrashedAt(this ServiceDatabase database, Guid id, DateTimeOffset at) =>
        database.Read(context => context.Documents.IgnoreQueryFilters()
            .Where(document => document.DocumentId == id && document.DeletedAt != null)
            .ExecuteUpdate(set => set.SetProperty(document => document.DeletedAt, at)));

    // Every row an article holds, so that a test can say none is left.
    public static string Held(this ServiceDatabase database, Guid id) => database.Read(context => string.Join(
        ',',
        context.Documents.IgnoreQueryFilters().Count(document => document.DocumentId == id),
        context.Rows("cms_article").Count(row => EF.Property<Guid>(row, StorageNames.DocumentId) == id),
        context.Rows("cms_article_i18n").Count(row => EF.Property<Guid>(row, StorageNames.DocumentId) == id),
        context.References.Count(reference => reference.SourceDocumentId == id),
        context.Versions.Count(version => version.DocumentId == id)));
}
