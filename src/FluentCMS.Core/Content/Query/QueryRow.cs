using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Content.Query;

// One entry in one state and locale, as the compiler sees it. A property bag has no CLR type of its own, so this
// holds the tables a query joins. Localized stays unset for a non-localized type.
internal sealed class QueryRow
{
    public Document Document { get; set; } = null!;

    public Dictionary<string, object> Shared { get; set; } = null!;

    public Dictionary<string, object> Localized { get; set; } = null!;
}

internal static class QueryRows
{
    // state is inlined, because a literal matches the WHERE state = 1 of the partial indexes as written. Trashed
    // documents are gone through the query filter on Document. An entry without a row in the locale and state is not
    // here at all: decision 44 filters on the requested locale only.
    public static IQueryable<QueryRow> Of(CmsDbContext context, SchemaType type, short state, string locale)
    {
        var documentLocale = type.Localized ? locale : "";

        var rows = context.Rows(StorageNames.Table(type))
            .Join(
                context.Documents,
                shared => EF.Property<Guid>(shared, StorageNames.DocumentId),
                document => document.DocumentId,
                (shared, document) => new QueryRow { Document = document, Shared = shared })
            .Where(row => EF.Property<short>(row.Shared, StorageNames.State) == EF.Constant(state) && row.Document.Locale == documentLocale);

        return !type.Localized
            ? rows
            : rows
                .Join(
                    context.Rows(StorageNames.LocaleTable(type)),
                    row => row.Document.DocumentId,
                    localized => EF.Property<Guid>(localized, StorageNames.DocumentId),
                    (row, localized) => new QueryRow { Document = row.Document, Shared = row.Shared, Localized = localized })
                .Where(row => EF.Property<short>(row.Localized, StorageNames.State) == EF.Constant(state)
                    && EF.Property<string>(row.Localized, StorageNames.Locale) == documentLocale);
    }
}
