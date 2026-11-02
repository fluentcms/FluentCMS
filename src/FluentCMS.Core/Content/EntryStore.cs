using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.ProseMirror;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace FluentCMS.Core.Content;

// The rows behind one entry: cms_document, the content tables, cms_reference and cms_version. It knows nothing of
// permissions; ContentService decides, and calls this inside its transaction. #2695 copies between the states with the same reads and writes.
internal sealed class EntryStore(CmsDbContext context, SchemaModel schema)
{
    private const string MediaTarget = "media";

    // The rows of an entry in the site the operation runs in, one per locale, oldest first. Other sites' entries and other types' are not there.
    public async Task<List<Document>> DocumentsAsync(SchemaType type, Guid id, SiteId site, bool trashed, CancellationToken cancellationToken)
    {
        var typeName = type.Name;
        var siteId = (Guid?)site.Value;
        IQueryable<Document> rows = trashed
            ? context.Documents.IgnoreQueryFilters().Where(document => document.DeletedAt != null)
            : context.Documents;

        rows = rows.AsNoTracking().Where(document => document.DocumentId == id && document.Type == typeName);
        if (type.Scope == ContentScope.Site)
        {
            rows = rows.Where(document => document.SiteId == siteId);
        }

        var found = await rows.ToListAsync(cancellationToken);
        return [.. found.OrderBy(document => document.Id)];
    }

    public Task<bool> AnyEntryAsync(SchemaType type, SiteId site, CancellationToken cancellationToken)
    {
        var typeName = type.Name;
        var siteId = (Guid?)site.Value;
        var rows = context.Documents.IgnoreQueryFilters().Where(document => document.Type == typeName);
        return type.Scope == ContentScope.Site
            ? rows.AnyAsync(document => document.SiteId == siteId, cancellationToken)
            : rows.AnyAsync(cancellationToken);
    }

    public async Task<int> DraftVersionAsync(Guid id, string locale, CancellationToken cancellationToken) =>
        await context.Documents.IgnoreQueryFilters().AsNoTracking()
            .Where(document => document.DocumentId == id && document.Locale == locale)
            .Select(document => document.DraftVersion)
            .FirstOrDefaultAsync(cancellationToken);

    // The entry as it is stored, hidden fields included, in the form EntryCanonicalizer gives. Not for a response.
    public async Task<ContentDocument> ReadAsync(SchemaType type, Document row, short state, CancellationToken cancellationToken)
    {
        var id = row.DocumentId;
        var locale = row.Locale;
        var shared = await RowAsync(StorageNames.Table(type), id, state, null, tracked: false, cancellationToken);
        var localized = type.Localized ? await RowAsync(StorageNames.LocaleTable(type), id, state, locale, tracked: false, cancellationToken) : null;

        var typeName = type.Name;
        var references = await context.References.AsNoTracking()
            .Where(reference => reference.SourceType == typeName
                && reference.SourceDocumentId == id
                && reference.State == state
                && (reference.Locale == "" || reference.Locale == locale))
            .OrderBy(reference => reference.Position)
            .ToListAsync(cancellationToken);
        var byField = references.ToLookup(reference => (reference.Field, reference.Locale));

        ContentDocument entry = [];
        ApplySystem(entry, row);
        FillFields(type, type, "", entry, shared, localized, byField, locale);
        return entry;
    }

    public static void ApplySystem(ContentDocument entry, Document row)
    {
        entry["id"] = row.DocumentId;
        entry["status"] = row.Status;
        entry["locale"] = row.Locale;
        entry["version"] = row.DraftVersion;
        entry["createdAt"] = row.CreatedAt;
        entry["updatedAt"] = row.UpdatedAt;
        entry["publishedAt"] = row.PublishedAt;
        entry["createdBy"] = row.CreatedBy;
        entry["updatedBy"] = row.UpdatedBy;
    }

    private void FillFields(
        SchemaType root,
        SchemaType owner,
        string prefix,
        ContentDocument into,
        Dictionary<string, object>? shared,
        Dictionary<string, object>? localized,
        ILookup<(string Field, string Locale), Reference> references,
        string locale)
    {
        var stored = FieldStorage.Of(schema, root).ToDictionary(item => QueryPaths.ApiPath(root, item), StringComparer.Ordinal);
        foreach (var field in owner.Fields)
        {
            var path = prefix + field.Name;
            if (field.FieldType == FieldTypeNames.Component && !field.Multiple)
            {
                ContentDocument group = [];
                var component = schema.FindComponent((string)field.Settings[FieldSettings.Component])!;
                FillFields(root, component, path + ".", group, shared, localized, references, locale);
                into[field.Name] = group.Values.All(value => value is null or JsonArray { Count: 0 }) ? null : group;
                continue;
            }

            var leaf = stored[path];
            if (leaf.Kind == FieldStorageKind.Reference)
            {
                var ids = references[(path, leaf.Localized ? locale : "")].Select(reference => reference.TargetId).ToArray();
                into[field.Name] = field.Multiple ? ids : ids.Length > 0 ? ids[0] : null;
                continue;
            }

            var row = leaf.Localized ? localized : shared;
            object? value = null;
            row?.TryGetValue(leaf.Column, out value);
            into[field.Name] = FromColumn(leaf, value);
        }
    }

    private static object? FromColumn(StoredField stored, object? value)
    {
        if (stored.Kind != FieldStorageKind.Json || value is not string json)
        {
            return value;
        }

        return stored.Field.FieldType == FieldTypeNames.RichText ? new RichText(json) : JsonNode.Parse(json);
    }

    // The compare and swap that makes one of two concurrent writers lose. The filter on Document keeps trash out.
    public async Task<bool> AdvanceVersionAsync(Guid id, string locale, int expected, EntryStatus status, DateTimeOffset at, Guid by, CancellationToken cancellationToken)
    {
        var next = expected + 1;
        var affected = await context.Documents
            .Where(document => document.DocumentId == id && document.Locale == locale && document.DraftVersion == expected)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(document => document.DraftVersion, next)
                    .SetProperty(document => document.Status, status)
                    .SetProperty(document => document.UpdatedAt, at)
                    .SetProperty(document => document.UpdatedBy, by),
                cancellationToken);

        return affected == 1;
    }

    // The draft of the shared row changed under the other locales, so their published state no longer equals it.
    public Task MarkOtherLocalesModifiedAsync(Guid id, string locale, CancellationToken cancellationToken) =>
        context.Documents
            .Where(document => document.DocumentId == id && document.Locale != locale && document.Status == EntryStatus.Published)
            .ExecuteUpdateAsync(set => set.SetProperty(document => document.Status, EntryStatus.Modified), cancellationToken);

    public Task SetTrashedAsync(Guid id, DateTimeOffset? at, Guid? by, CancellationToken cancellationToken) =>
        context.Documents.IgnoreQueryFilters()
            .Where(document => document.DocumentId == id)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(document => document.DeletedAt, at)
                    .SetProperty(document => document.DeletedBy, by),
                cancellationToken);

    // Saves the draft of one locale: the document when it is new, the shared row, the locale row, the references and the
    // version, in one save. The context may hold what a caller has not saved, in a transaction of its own: that is not
    // this write's to save, or to lose if this write is rolled back, so it stays pending and only this write's rows go.
    public async Task WriteDraftAsync(SchemaType type, Document row, bool isNewDocument, ContentDocument entry, Origin origin, string? clientId, CancellationToken cancellationToken)
    {
        var held = HeldChanges.Take(context);
        try
        {
            if (isNewDocument)
            {
                context.Documents.Add(row);
            }

            var stored = FieldStorage.Of(schema, type).ToList();
            await WriteRowAsync(StorageNames.Table(type), type, row, null, stored.Where(item => !item.Localized), entry, cancellationToken);
            if (type.Localized)
            {
                await WriteRowAsync(StorageNames.LocaleTable(type), type, row, row.Locale, stored.Where(item => item.Localized), entry, cancellationToken);
            }

            await ReplaceReferencesAsync(type, row, stored, entry, cancellationToken);

            context.Versions.Add(new ContentVersion
            {
                DocumentId = row.DocumentId,
                Locale = row.Locale,
                Number = row.DraftVersion,
                Snapshot = EntryJson.Text(EntryJson.Fields(type, entry)),
                StatusAfter = row.Status,
                AuthorId = row.UpdatedBy,
                Origin = origin,
                ClientId = clientId,
                CreatedAt = row.UpdatedAt,
            });

            await context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            held.Release();
        }
    }

    // Which of these relation targets exist, in the trash too. A media target has no row here, so it counts.
    public async Task<List<Guid>> ExistingTargetsAsync(SchemaField field, List<Guid> ids, CancellationToken cancellationToken)
    {
        if (schema.FindContentType(EntryPaths.ReferenceTarget(field)) is null)
        {
            return ids;
        }

        return await context.Documents.IgnoreQueryFilters().AsNoTracking()
            .Where(document => ids.Contains(document.DocumentId))
            .Select(document => document.DocumentId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    private async Task WriteRowAsync(string table, SchemaType type, Document document, string? locale, IEnumerable<StoredField> fields, ContentDocument entry, CancellationToken cancellationToken)
    {
        var row = await RowAsync(table, document.DocumentId, ContentState.Draft, locale, tracked: true, cancellationToken);
        var isNew = row is null;
        row ??= new Dictionary<string, object>
        {
            [StorageNames.DocumentId] = document.DocumentId,
            [StorageNames.State] = ContentState.Draft,
        };

        if (isNew && document.SiteId is { } site)
        {
            row[StorageNames.SiteId] = site;
        }

        if (isNew && locale is not null)
        {
            row[StorageNames.Locale] = locale;
        }

        foreach (var stored in fields.Where(item => item.Kind != FieldStorageKind.Reference))
        {
            row[stored.Column] = ToColumn(stored, EntryPaths.ValueAt(entry, stored, type))!;
        }

        if (isNew)
        {
            context.Rows(table).Add(row);
        }
    }

    private Task<Dictionary<string, object>?> RowAsync(string table, Guid id, short state, string? locale, bool tracked, CancellationToken cancellationToken)
    {
        IQueryable<Dictionary<string, object>> rows = tracked ? context.Rows(table) : context.Rows(table).AsNoTracking();
        rows = rows.Where(row => EF.Property<Guid>(row, StorageNames.DocumentId) == id && EF.Property<short>(row, StorageNames.State) == state);
        if (locale is not null)
        {
            rows = rows.Where(row => EF.Property<string>(row, StorageNames.Locale) == locale);
        }

        return rows.FirstOrDefaultAsync(cancellationToken);
    }

    private static object? ToColumn(StoredField stored, object? value)
    {
        if (value is null)
        {
            return stored.Required ? Placeholder(stored) : null;
        }

        if (stored.Kind == FieldStorageKind.Json)
        {
            return value is RichText document ? document.Json : EntryJson.Text(EntryJson.ToNode(value)!);
        }

        var target = Nullable.GetUnderlyingType(stored.ClrType!) ?? stored.ClrType!;
        if (target.IsInstanceOfType(value))
        {
            return value;
        }

        return target == typeof(string)
            ? Convert.ToString(value, CultureInfo.InvariantCulture)
            : Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
    }

    private static string Placeholder(StoredField stored)
    {
        if (stored.Kind != FieldStorageKind.Json)
        {
            return "";
        }

        return stored.Field.FieldType == FieldTypeNames.RichText ? RichText.Empty.Json : stored.DefaultValue ?? "[]";
    }

    // A reference inside JSON or rich text has no output of its own: it is stored under the field that holds it, which
    // the query engine does not read for a relation, so that "what references X" and integrity on publish see it too.
    private async Task ReplaceReferencesAsync(SchemaType type, Document row, List<StoredField> stored, ContentDocument entry, CancellationToken cancellationToken)
    {
        var typeName = type.Name;
        var id = row.DocumentId;
        var locale = row.Locale;
        await context.References
            .Where(reference => reference.SourceType == typeName
                && reference.SourceDocumentId == id
                && reference.State == ContentState.Draft
                && (reference.Locale == "" || reference.Locale == locale))
            .ExecuteDeleteAsync(cancellationToken);

        foreach (var item in stored)
        {
            var value = EntryPaths.ValueAt(entry, item, type);
            List<(string Target, Guid Id)> targets = [];
            Collect(item.Field, value, targets);

            var position = 0;
            foreach (var (target, targetId) in targets)
            {
                context.References.Add(new Reference
                {
                    SourceType = typeName,
                    SourceDocumentId = id,
                    State = ContentState.Draft,
                    Locale = item.Localized ? locale : "",
                    Field = QueryPaths.ApiPath(type, item),
                    Position = position++,
                    TargetType = target,
                    TargetId = targetId,
                });
            }
        }
    }

    private void Collect(SchemaField field, object? value, List<(string Target, Guid Id)> into)
    {
        if (schema.FieldTypes.Find(field.FieldType)!.StorageKind(field) == FieldStorageKind.Reference)
        {
            CollectReference(field, value, into);
        }
        else if (field.FieldType == FieldTypeNames.RichText)
        {
            CollectRichText(value, into);
        }
        else if (value is JsonNode node)
        {
            CollectInside(field, node, into);
        }
    }

    private static void CollectReference(SchemaField field, object? value, List<(string Target, Guid Id)> into)
    {
        var target = EntryPaths.ReferenceTarget(field);
        switch (value)
        {
            case Guid id:
                into.Add((target, id));
                break;
            case Guid[] ids:
                into.AddRange(ids.Select(id => (target, id)));
                break;
            case JsonArray items:
                foreach (var item in items)
                {
                    CollectReference(field, item, into);
                }

                break;
            case JsonNode node when EntryCanonicalizer.TryReadId(RawValue.Unwrap(node), out var id):
                into.Add((target, id));
                break;
        }
    }

    private static void CollectRichText(object? value, List<(string Target, Guid Id)> into)
    {
        var document = value switch
        {
            RichText text => text,
            JsonObject node => new RichText(EntryJson.Text(node)),
            _ => null,
        };

        if (document is null)
        {
            return;
        }

        var found = RichTextReferenceExtractor.Extract(document);
        into.AddRange(found.Entries.Select(entry => (entry.Type, entry.Id)));
        into.AddRange(found.Media.Select(media => (MediaTarget, media.Id)));
    }

    private void CollectInside(SchemaField field, JsonNode node, List<(string Target, Guid Id)> into)
    {
        foreach (var item in node is JsonArray items ? items.OfType<JsonObject>() : node is JsonObject single ? [single] : [])
        {
            SchemaType? owner = field.FieldType switch
            {
                FieldTypeNames.Component => schema.FindComponent((string)field.Settings[FieldSettings.Component]),
                FieldTypeNames.Blocks when item["type"] is JsonValue name && name.TryGetValue<string>(out var block) => schema.FindBlock(block),
                _ => null,
            };

            foreach (var inner in owner?.Fields ?? [])
            {
                if (item[inner.Name] is { } value)
                {
                    Collect(inner, value, into);
                }
            }
        }
    }

    // Against both states, because a draft that takes a published value would only fail at publish. Against the trash too,
    // because the unique index of the published state knows nothing of it, and a restore would bring the value back.
    // An empty value is what an unfinished draft holds, and is not a value to clash on.
    public async Task CheckUniqueAsync(SchemaType type, ContentDocument entry, Guid id, SiteId site, string locale, ValidationContext result, CancellationToken cancellationToken)
    {
        foreach (var stored in FieldStorage.Of(schema, type).Where(item => item.Field.Unique && item.Kind == FieldStorageKind.Column))
        {
            var value = ToColumn(stored, EntryPaths.ValueAt(entry, stored, type));
            if (value is null or "")
            {
                continue;
            }

            var table = stored.Localized ? StorageNames.LocaleTable(type) : StorageNames.Table(type);
            var row = Expression.Parameter(typeof(Dictionary<string, object>), "row");
            Expression taken = Expression.AndAlso(
                Expression.Equal(QueryExpressions.Column(row, stored.Column, stored.ClrType!), QueryExpressions.Bind(value, stored.ClrType!)),
                Expression.NotEqual(QueryExpressions.Column(row, StorageNames.DocumentId, typeof(Guid)), QueryExpressions.Bind(id)));

            if (type.Scope == ContentScope.Site)
            {
                taken = Expression.AndAlso(taken, Expression.Equal(QueryExpressions.Column(row, StorageNames.SiteId, typeof(Guid)), QueryExpressions.Bind(site.Value)));
            }

            if (stored.Localized)
            {
                taken = Expression.AndAlso(taken, Expression.Equal(QueryExpressions.Column(row, StorageNames.Locale, typeof(string)), QueryExpressions.Bind(locale)));
            }

            var predicate = Expression.Lambda<Func<Dictionary<string, object>, bool>>(taken, row);
            if (await context.Rows(table).AsNoTracking().AnyAsync(predicate, cancellationToken))
            {
                var path = QueryPaths.ApiPath(type, stored);
                result.AddError(path, ValidationErrorCodes.NotUnique, $"Another entry already has this value of {path}.");
            }
        }
    }
}
