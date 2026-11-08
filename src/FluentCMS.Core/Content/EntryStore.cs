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

namespace FluentCMS.Core.Content;

// The rows behind one entry: cms_document, the content tables, cms_reference and cms_version. It knows nothing of
// permissions; ContentService decides, and calls this inside its transaction.
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

        // Not by id alone: a generated id is time-ordered on SQLite and PostgreSQL only, and .NET orders it differently from SQL Server.
        return await rows.OrderBy(document => document.CreatedAt).ThenBy(document => document.Id).ToListAsync(cancellationToken);
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
    public async Task<ContentDocument> ReadAsync(SchemaType type, Document row, short state, CancellationToken cancellationToken) =>
        (await ReadStoredAsync(type, row, state, cancellationToken)).Entry;

    // The draft and the rows it was read from, so that writing over it in the same transaction need not read them again.
    public Task<StoredDraft> ReadDraftAsync(SchemaType type, Document row, CancellationToken cancellationToken) =>
        ReadStoredAsync(type, row, ContentState.Draft, cancellationToken);

    private async Task<StoredDraft> ReadStoredAsync(SchemaType type, Document row, short state, CancellationToken cancellationToken)
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
        return new StoredDraft(entry, new StoredRows(shared, localized));
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
        var stored = FieldStorage.ByPath(schema, root);
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

        return stored.Field.FieldType == FieldTypeNames.RichText ? new RichText(json) : JsonDepth.Parse(json);
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

    public Task SetTrashedAsync(Guid id, DateTimeOffset? at, Guid? by, CancellationToken cancellationToken) =>
        context.Documents.IgnoreQueryFilters()
            .Where(document => document.DocumentId == id)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(document => document.DeletedAt, at)
                    .SetProperty(document => document.DeletedBy, by),
                cancellationToken);

    // When and by whom each of these entries was trashed, in one locale. A trashed row is not read through the query filter.
    public async Task<Dictionary<Guid, (DateTimeOffset? At, Guid? By)>> TrashMarksAsync(IReadOnlyCollection<Guid> ids, string locale, CancellationToken cancellationToken)
    {
        var marks = await context.Documents.IgnoreQueryFilters().AsNoTracking()
            .Where(document => ids.Contains(document.DocumentId) && document.Locale == locale && document.DeletedAt != null)
            .Select(document => new { document.DocumentId, document.DeletedAt, document.DeletedBy })
            .ToListAsync(cancellationToken);

        return marks.ToDictionary(mark => mark.DocumentId, mark => (mark.DeletedAt, mark.DeletedBy));
    }

    // Everything an entry holds, in every state and locale. The documents go first and only while they are in the trash
    // and, when a cutoff is given, were put there before it, so that an entry trashed again since it was read as due is not
    // taken; the caller compares the count with the rows it saw and rolls back on a difference. References to the entry from
    // others are theirs: they are the values of those entries' fields, and a read leaves out a target that is not there.
    public async Task<int> PurgeAsync(SchemaType type, Guid id, DateTimeOffset? trashedBefore, CancellationToken cancellationToken)
    {
        var typeName = type.Name;
        var documents = context.Documents.IgnoreQueryFilters().Where(document => document.DocumentId == id && document.Type == typeName && document.DeletedAt != null);
        if (trashedBefore is { } before)
        {
            documents = documents.Where(document => document.DeletedAt < before);
        }

        var removed = await documents.ExecuteDeleteAsync(cancellationToken);
        await PurgeSchedulesAsync(id, cancellationToken);
        await context.Versions.Where(version => version.DocumentId == id).ExecuteDeleteAsync(cancellationToken);
        await context.References.Where(reference => reference.SourceType == typeName && reference.SourceDocumentId == id).ExecuteDeleteAsync(cancellationToken);
        if (type.Localized)
        {
            await context.Rows(StorageNames.LocaleTable(type)).Where(row => EF.Property<Guid>(row, StorageNames.DocumentId) == id).ExecuteDeleteAsync(cancellationToken);
        }

        await context.Rows(StorageNames.Table(type)).Where(row => EF.Property<Guid>(row, StorageNames.DocumentId) == id).ExecuteDeleteAsync(cancellationToken);
        return removed;
    }

    // A schedule goes with its entry, and its job if it has not started. One that has will find nothing to run.
    private async Task PurgeSchedulesAsync(Guid id, CancellationToken cancellationToken)
    {
        var jobs = context.Set<ScheduleRow>().Where(row => row.DocumentId == id && row.Status == ScheduleRow.Pending && row.JobId != null).Select(row => row.JobId);
        await context.Set<Job>().Where(job => job.Status == JobClaimStatus.Pending && jobs.Contains(job.Id)).ExecuteDeleteAsync(cancellationToken);
        await context.Set<ScheduleRow>().Where(row => row.DocumentId == id).ExecuteDeleteAsync(cancellationToken);
    }

    // Saves the draft of one locale: the document when it is new, the shared row, the locale row, the references and the
    // version, in one save, and prunes the versions over the cap of the type. over holds the draft rows this transaction
    // read, which are the rows written over, and fields is EntryJson.Fields of the entry.
    public Task WriteDraftAsync(SchemaType type, Document row, bool isNewDocument, StoredRows over, ContentDocument entry, JsonObject fields, Origin origin, string? clientId, CancellationToken cancellationToken, string? message = null) =>
        SaveOwnRowsAsync(
            async () =>
            {
                if (isNewDocument)
                {
                    context.Documents.Add(row);
                }

                var stored = FieldStorage.Of(schema, type);
                WriteRow(StorageNames.Table(type), type, row, null, stored.Where(item => !item.Localized), entry, over.Shared);
                if (type.Localized)
                {
                    WriteRow(StorageNames.LocaleTable(type), type, row, row.Locale, stored.Where(item => item.Localized), entry, over.Localized);
                }

                await ReplaceReferencesAsync(type, row, stored, entry, cancellationToken);

                context.Versions.Add(new ContentVersion
                {
                    DocumentId = row.DocumentId,
                    Locale = row.Locale,
                    Number = row.DraftVersion,
                    Snapshot = EntryJson.Text(fields),
                    StatusAfter = row.Status == EntryStatus.Published ? EntryStatus.Modified : row.Status,
                    AuthorId = row.UpdatedBy,
                    Origin = origin,
                    ClientId = clientId,
                    Message = message,
                    CreatedAt = row.UpdatedAt,
                });
            },
            () => PruneVersionsAsync(type, row, cancellationToken),
            cancellationToken);

    // Copies the draft rows of one locale, and the shared row they sit on, over the published ones.
    public Task CopyDraftToPublishedAsync(SchemaType type, Document row, CancellationToken cancellationToken) =>
        SaveOwnRowsAsync(
            async () =>
            {
                await CopyRowAsync(StorageNames.Table(type), row.DocumentId, null, cancellationToken);
                if (type.Localized)
                {
                    await CopyRowAsync(StorageNames.LocaleTable(type), row.DocumentId, row.Locale, cancellationToken);
                }

                await DeleteReferencesAsync(type, row, ContentState.Published, includingShared: true, cancellationToken);
                var drafts = await ReferencesOf(type, row, ContentState.Draft, includingShared: true).AsNoTracking().ToListAsync(cancellationToken);
                context.References.AddRange(drafts.Select(reference => new Reference
                {
                    SourceType = reference.SourceType,
                    SourceDocumentId = reference.SourceDocumentId,
                    State = ContentState.Published,
                    Locale = reference.Locale,
                    Field = reference.Field,
                    Position = reference.Position,
                    TargetType = reference.TargetType,
                    TargetId = reference.TargetId,
                }));
            },
            null,
            cancellationToken);

    // Removes the published rows of one locale. The shared row and its references serve the other published locales, so they go only with the last.
    public async Task DeletePublishedAsync(SchemaType type, Document row, bool isLastPublished, CancellationToken cancellationToken)
    {
        if (type.Localized)
        {
            await PublishedRows(StorageNames.LocaleTable(type), row.DocumentId, row.Locale).ExecuteDeleteAsync(cancellationToken);
        }

        if (!type.Localized || isLastPublished)
        {
            await PublishedRows(StorageNames.Table(type), row.DocumentId, null).ExecuteDeleteAsync(cancellationToken);
        }

        await DeleteReferencesAsync(type, row, ContentState.Published, includingShared: !type.Localized || isLastPublished, cancellationToken);
    }

    // A draft version the row must still hold: one that lost the swap has changed nothing.
    public async Task<bool> MarkPublishedAsync(Document row, DateTimeOffset at, Guid by, CancellationToken cancellationToken)
    {
        var id = row.DocumentId;
        var locale = row.Locale;
        var version = row.DraftVersion;
        var first = row.FirstPublishedAt ?? at;
        var affected = await context.Documents
            .Where(document => document.DocumentId == id && document.Locale == locale && document.DraftVersion == version)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(document => document.Status, EntryStatus.Published)
                    .SetProperty(document => document.PublishedVersion, version)
                    .SetProperty(document => document.PublishedAt, at)
                    .SetProperty(document => document.FirstPublishedAt, first)
                    .SetProperty(document => document.UpdatedAt, at)
                    .SetProperty(document => document.UpdatedBy, by),
                cancellationToken);

        if (affected != 1)
        {
            return false;
        }

        // Pruning goes by this: a version that was published stays.
        await context.Versions
            .Where(item => item.DocumentId == id && item.Locale == locale && item.Number == version)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.StatusAfter, EntryStatus.Published), cancellationToken);

        row.Status = EntryStatus.Published;
        row.PublishedVersion = version;
        row.PublishedAt = at;
        row.FirstPublishedAt = first;
        row.UpdatedAt = at;
        row.UpdatedBy = by;
        return true;
    }

    public async Task<bool> MarkUnpublishedAsync(Document row, DateTimeOffset at, Guid by, CancellationToken cancellationToken)
    {
        var id = row.DocumentId;
        var locale = row.Locale;
        var version = row.PublishedVersion;
        var affected = await context.Documents
            .Where(document => document.DocumentId == id && document.Locale == locale && document.PublishedVersion == version)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(document => document.Status, EntryStatus.Unpublished)
                    .SetProperty(document => document.PublishedVersion, (int?)null)
                    .SetProperty(document => document.PublishedAt, (DateTimeOffset?)null)
                    .SetProperty(document => document.UpdatedAt, at)
                    .SetProperty(document => document.UpdatedBy, by),
                cancellationToken);

        if (affected != 1)
        {
            return false;
        }

        row.Status = EntryStatus.Unpublished;
        row.PublishedVersion = null;
        row.PublishedAt = null;
        row.UpdatedAt = at;
        row.UpdatedBy = by;
        return true;
    }

    public Task SetStatusAsync(Guid id, string locale, EntryStatus status, CancellationToken cancellationToken) =>
        context.Documents
            .Where(document => document.DocumentId == id && document.Locale == locale)
            .ExecuteUpdateAsync(set => set.SetProperty(document => document.Status, status), cancellationToken);

    // Without the snapshot, the large column of cms_version.
    public Task<List<VersionInfo>> VersionsAsync(Guid id, string locale, CancellationToken cancellationToken) =>
        context.Versions.AsNoTracking()
            .Where(version => version.DocumentId == id && version.Locale == locale)
            .OrderByDescending(version => version.Number)
            .Select(version => new VersionInfo(version.Number, version.Locale, version.StatusAfter, version.AuthorId, version.Origin, version.ClientId, version.Message, version.CreatedAt))
            .ToListAsync(cancellationToken);

    public Task<ContentVersion?> VersionAsync(Guid id, string locale, int number, CancellationToken cancellationToken) =>
        context.Versions.AsNoTracking()
            .FirstOrDefaultAsync(version => version.DocumentId == id && version.Locale == locale && version.Number == number, cancellationToken);

    // The rows of targets of one type, one per locale, in one query. The query filter on Document leaves out the trash, and
    // a target of another type or another site has none.
    public async Task<Dictionary<Guid, List<ReferenceTargetRow>>> TargetRowsAsync(SchemaType target, IReadOnlyCollection<Guid> ids, SiteId site, CancellationToken cancellationToken)
    {
        var typeName = target.Name;
        var siteId = (Guid?)site.Value;
        List<Guid> wanted = [.. ids.Distinct()];
        var rows = context.Documents.AsNoTracking().Where(document => wanted.Contains(document.DocumentId) && document.Type == typeName);
        if (target.Scope == ContentScope.Site)
        {
            rows = rows.Where(document => document.SiteId == siteId);
        }

        var found = await rows.Select(document => new { document.DocumentId, document.Locale, IsPublished = document.PublishedVersion != null }).ToListAsync(cancellationToken);
        return found.GroupBy(row => row.DocumentId).ToDictionary(group => group.Key, group => group.Select(row => new ReferenceTargetRow(row.Locale, row.IsPublished)).ToList());
    }

    // What an entry refers to, with whether the reference is outside what hidden fields hide. Media has no row here to check.
    public List<EntryTarget> TargetsOf(SchemaType type, ContentDocument entry)
    {
        List<EntryTarget> targets = [];
        foreach (var item in FieldStorage.Of(schema, type))
        {
            var path = QueryPaths.ApiPath(type, item);
            var value = EntryPaths.ValueAt(entry, item, type);
            List<(string Target, Guid Id)> all = [];
            Collect(item.Field, value, all);
            if (all.Count == 0)
            {
                continue;
            }

            var top = path.Split('.')[0];
            List<(string Target, Guid Id)> shown = [];
            if (!item.Field.Hidden && type.FindField(top) is { Hidden: false })
            {
                Collect(item.Field, WithoutHidden(item.Field, value), shown);
            }

            foreach (var (target, id) in all.Where(found => found.Target != MediaTarget))
            {
                targets.Add(new EntryTarget(path, top, target, id, shown.Remove((target, id))));
            }
        }

        return targets;
    }

    private object? WithoutHidden(SchemaField field, object? value)
    {
        if (value is RichText document)
        {
            return HiddenFields.Remove(schema, document);
        }

        if (value is not JsonNode node)
        {
            return value;
        }

        var visible = node.DeepClone();
        HiddenFields.Remove(schema, visible, field);
        return visible;
    }

    // The context may hold what a caller has not saved, in a transaction of its own: that is not this write's to save, or
    // to lose if this write is rolled back, so it stays pending and only this write's rows go. The content service's
    // transaction or savepoint takes back a save that fails, so the savepoint EF would put around it is two round trips
    // that undo nothing more.
    private async Task SaveOwnRowsAsync(Func<Task> add, Func<Task>? afterSave, CancellationToken cancellationToken)
    {
        var held = HeldChanges.Take(context);
        var savepoints = context.Database.AutoSavepointsEnabled;
        try
        {
            await add();
            context.Database.AutoSavepointsEnabled = false;
            await context.SaveChangesAsync(cancellationToken);
            context.Database.AutoSavepointsEnabled = savepoints;
            if (afterSave is not null)
            {
                await afterSave();
            }
        }
        finally
        {
            context.Database.AutoSavepointsEnabled = savepoints;
            held.Release();
        }
    }

    // The oldest go first and the cap counts every version, so an entry with more published versions than the cap keeps
    // those and no other. The one just written stays whatever its status. Versions are numbered by the draft version and
    // the numbers are unique, so an entry whose draft version is within the cap has nothing to prune.
    private async Task PruneVersionsAsync(SchemaType type, Document row, CancellationToken cancellationToken)
    {
        var id = row.DocumentId;
        var locale = row.Locale;
        var number = row.DraftVersion;
        if (number <= type.MaxVersions)
        {
            return;
        }

        var versions = context.Versions.Where(version => version.DocumentId == id && version.Locale == locale);
        var excess = await versions.CountAsync(cancellationToken) - type.MaxVersions;
        if (excess <= 0)
        {
            return;
        }

        var prunable = await versions
            .Where(version => version.StatusAfter != EntryStatus.Published && version.Number != number)
            .Select(version => new { version.Id, version.Number })
            .ToListAsync(cancellationToken);
        var oldest = prunable.OrderBy(version => version.Number).Take(excess).Select(version => version.Id).ToList();
        await context.Versions.Where(version => oldest.Contains(version.Id)).ExecuteDeleteAsync(cancellationToken);
    }

    private async Task CopyRowAsync(string table, Guid id, string? locale, CancellationToken cancellationToken)
    {
        var draft = await RowAsync(table, id, ContentState.Draft, locale, tracked: false, cancellationToken);
        await PublishedRows(table, id, locale).ExecuteDeleteAsync(cancellationToken);
        if (draft is null)
        {
            return;
        }

        Dictionary<string, object> published = new(draft);
        published.Remove(StorageNames.Id);
        published[StorageNames.State] = ContentState.Published;
        context.Rows(table).Add(published);
    }

    private IQueryable<Dictionary<string, object>> PublishedRows(string table, Guid id, string? locale)
    {
        var rows = context.Rows(table).Where(row => EF.Property<Guid>(row, StorageNames.DocumentId) == id && EF.Property<short>(row, StorageNames.State) == ContentState.Published);
        return locale is null ? rows : rows.Where(row => EF.Property<string>(row, StorageNames.Locale) == locale);
    }

    private IQueryable<Reference> ReferencesOf(SchemaType type, Document row, short state, bool includingShared)
    {
        var typeName = type.Name;
        var id = row.DocumentId;
        var locale = row.Locale;
        return context.References.Where(reference => reference.SourceType == typeName
            && reference.SourceDocumentId == id
            && reference.State == state
            && ((includingShared && reference.Locale == "") || reference.Locale == locale));
    }

    private Task<int> DeleteReferencesAsync(SchemaType type, Document row, short state, bool includingShared, CancellationToken cancellationToken) =>
        ReferencesOf(type, row, state, includingShared).ExecuteDeleteAsync(cancellationToken);

    // Which of these ids are entries of this type in this site, in the trash too.
    public async Task<List<Guid>> ExistingTargetsAsync(SchemaType target, List<Guid> ids, SiteId site, CancellationToken cancellationToken)
    {
        var typeName = target.Name;
        var siteId = (Guid?)site.Value;
        var rows = context.Documents.IgnoreQueryFilters().AsNoTracking().Where(document => ids.Contains(document.DocumentId) && document.Type == typeName);
        if (target.Scope == ContentScope.Site)
        {
            rows = rows.Where(document => document.SiteId == siteId);
        }

        return await rows.Select(document => document.DocumentId).Distinct().ToListAsync(cancellationToken);
    }

    // read is the draft row as this transaction read it, or null when it had none. Every field column is written, so the
    // save sets them all whatever changed since the read, as a save over a row read just now would.
    private void WriteRow(string table, SchemaType type, Document document, string? locale, IEnumerable<StoredField> fields, ContentDocument entry, Dictionary<string, object>? read)
    {
        var row = Track(table, read);
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

        List<string> columns = [];
        foreach (var stored in fields.Where(item => item.Kind != FieldStorageKind.Reference))
        {
            row[stored.Column] = ToColumn(stored, EntryPaths.ValueAt(entry, stored, type))!;
            columns.Add(stored.Column);
        }

        if (isNew)
        {
            context.Rows(table).Add(row);
            return;
        }

        var tracked = context.Entry(row);
        foreach (var column in columns)
        {
            tracked.Property(column).IsModified = true;
        }
    }

    // The instance the context tracks for that row, as a tracked query would give it, or the row as read, attached.
    private Dictionary<string, object>? Track(string table, Dictionary<string, object>? read)
    {
        if (read is null)
        {
            return null;
        }

        var rows = context.Rows(table);
        if (rows.Local.FindEntry((Guid)read[StorageNames.Id]) is { } tracked)
        {
            return tracked.Entity;
        }

        Dictionary<string, object> row = new(read);
        rows.Attach(row);
        return row;
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
    private async Task ReplaceReferencesAsync(SchemaType type, Document row, IReadOnlyList<StoredField> stored, ContentDocument entry, CancellationToken cancellationToken)
    {
        var typeName = type.Name;
        var id = row.DocumentId;
        var locale = row.Locale;
        await DeleteReferencesAsync(type, row, ContentState.Draft, includingShared: true, cancellationToken);

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

    private void CollectRichText(object? value, List<(string Target, Guid Id)> into)
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
        foreach (var block in found.InlineBlocks)
        {
            foreach (var field in schema.FindBlock(block.BlockType)?.Fields ?? [])
            {
                if (block.Data.TryGetValue(field.Name, out var data))
                {
                    Collect(field, EntryJson.ToNode(data), into);
                }
            }
        }
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
    // An empty value is what an unfinished draft holds, and is not a value to clash on, until it is published: the unique
    // index of that state has room for one ''.
    public async Task CheckUniqueAsync(SchemaType type, ContentDocument entry, Guid id, SiteId site, string locale, ValidationContext result, CancellationToken cancellationToken, bool publishing = false)
    {
        foreach (var stored in FieldStorage.Of(schema, type).Where(item => item.Field.Unique && item.Kind == FieldStorageKind.Column))
        {
            var value = ToColumn(stored, EntryPaths.ValueAt(entry, stored, type));
            if (value is null || (value is "" && !publishing))
            {
                continue;
            }

            var table = stored.Localized ? StorageNames.LocaleTable(type) : StorageNames.Table(type);
            var row = Expression.Parameter(typeof(Dictionary<string, object>), "row");
            Expression taken = Expression.AndAlso(
                Expression.Equal(QueryExpressions.Column(row, stored.Column, stored.ClrType!), QueryExpressions.Bind(value, stored.ClrType!)),
                Expression.NotEqual(QueryExpressions.Column(row, StorageNames.DocumentId, typeof(Guid)), QueryExpressions.Bind(id)));

            if (value is "")
            {
                taken = Expression.AndAlso(taken, Expression.Equal(QueryExpressions.Column(row, StorageNames.State, typeof(short)), QueryExpressions.Bind(ContentState.Published)));
            }

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

internal enum ReferenceTargetState
{
    Unavailable,
    Unpublished,
    Published,
}

internal sealed record ReferenceTargetRow(string Locale, bool IsPublished);

// The content rows of one draft as a transaction read them: the shared row, and the locale row of a localized type. Null
// is no row. None is a draft that has no rows yet.
internal sealed record StoredRows(Dictionary<string, object>? Shared, Dictionary<string, object>? Localized)
{
    public static readonly StoredRows None = new(null, null);
}

internal sealed record StoredDraft(ContentDocument Entry, StoredRows Rows);

// One reference of an entry. Path is the stored field, Field the top-level field that holds it.
internal sealed record EntryTarget(string Path, string Field, string TargetType, Guid Id, bool IsShown);
