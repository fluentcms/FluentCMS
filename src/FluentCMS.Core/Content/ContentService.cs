using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content.Hooks;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Content.Validation;
using FluentCMS.Core.Localization;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Localization;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Content;

// Where permissions, field limits, hooks, validation and versions meet. A write goes: the permission on the type, then
// in one transaction the entry and its row rule, the expected version, the field limits, BeforeValidate, validation,
// BeforeCreate or BeforeUpdate, the write and the AfterX hooks, and only after the commit the change sink and the
// response, which is read back like any other read so that a hook's values cannot reach it. A call from a hook joins the transaction.
internal sealed class ContentService(
    CmsDbContext context,
    SchemaModel schema,
    IOperationContextAccessor accessor,
    IAuthorizationEvaluator evaluator,
    ContentQueryEngine engine,
    LocaleFallbackReader fallback,
    ILocaleResolver locales,
    HookPipeline hooks,
    ValidationPipeline validation,
    EntryStore store,
    EntryCanonicalizer canonicalizer,
    EntryComposer composer,
    ContentEntryMapper mapper,
    ResponseRichText richText,
    IEnumerable<IEntryChangeSink> sinks) : IContentService
{
    private readonly List<EntryChange> _pending = [];

    public Task<ContentDocument?> GetAsync(string type, Guid id, GetOptions? options = null, CancellationToken cancellationToken = default) =>
        GetAsync(type, id, options, everySystemField: false, cancellationToken);

    public async Task<ContentDocument?> GetAsync(string type, Guid id, GetOptions? options, bool everySystemField, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(type);

        options ??= new GetOptions();
        var query = new ContentQuery
        {
            Filter = new FieldFilter("id", FilterOperator.Equal, id),
            Page = new PageRequest { Size = 1, WithTotal = false },
            Select = options.Select,
            Populate = options.Populate,
            PopulateDepth = options.PopulateDepth,
            Status = options.Status,
            Locale = options.Locale,
            Fallback = options.Fallback,
            RichTextFormat = options.RichTextFormat,
        };

        var items = (await ReadAsync(type, query, id, everySystemField, cancellationToken)).Items;
        return items.Count > 0 ? items[0] : null;
    }

    public Task<PagedResult<ContentDocument>> QueryAsync(string type, ContentQuery query, CancellationToken cancellationToken = default) =>
        QueryAsync(type, query, everySystemField: false, cancellationToken);

    public Task<PagedResult<ContentDocument>> QueryAsync(string type, ContentQuery query, bool everySystemField, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(query);

        return ReadAsync(type, query, null, everySystemField, cancellationToken);
    }

    public async Task<long> CountAsync(string type, ContentFilter? filter = null, QueryStatus status = QueryStatus.Published, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);

        var operation = Current();
        var query = new ContentQuery
        {
            Filter = filter,
            Page = new PageRequest { Size = 1, WithTotal = true },
            Select = ["id"],
            Status = status,
            Site = operation.Site,
            Locale = await ResolveLocaleAsync(operation.Site, operation.Locale, cancellationToken),
            Fallback = LocaleFallback.None,
        };

        return (await engine.ExecuteAsync(type, query, cancellationToken)).Total ?? 0;
    }

    public async Task<ContentDocument> CreateAsync(string type, ContentDocument entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(entry);

        var write = await BeginAsync(type, ContentOperation.Create, cancellationToken);
        var id = Guid.CreateVersion7();
        var row = await InTransactionAsync(
            async () =>
            {
                var writable = await evaluator.WritableFieldsAsync(write.Operation, write.Type, ContentOperation.Create, null, write.Target, cancellationToken);
                Reject(write, entry, writable);

                if (write.Type.Kind == SchemaTypeKind.Singleton && await store.AnyEntryAsync(write.Type, write.Site, cancellationToken))
                {
                    throw new ValidationException([new ValidationError("", ContentErrorCodes.SingletonExists, $"{write.Type.Name} is a singleton and already has its entry.")]);
                }

                var document = new Document
                {
                    DocumentId = id,
                    Locale = write.DocumentLocale,
                    Type = write.Type.Name,
                    SiteId = write.DocumentSite,
                    Status = EntryStatus.Draft,
                    DraftVersion = 1,
                    CreatedAt = write.Operation.Timestamp,
                    CreatedBy = write.Operation.Principal.Id,
                    UpdatedAt = write.Operation.Timestamp,
                    UpdatedBy = write.Operation.Principal.Id,
                };

                ContentDocument composed = new(entry);
                foreach (var field in write.Type.Fields)
                {
                    if (field.DefaultValue is { } value && !composed.ContainsKey(field.Name))
                    {
                        composed[field.Name] = value;
                    }
                }

                var working = Prepare(write, composed, document);
                working = await RunBeforeHooksAsync(write, HookKind.BeforeCreate, working, null, writable, document, cancellationToken);

                store.AddDocument(document);
                await store.WriteDraftAsync(write.Type, document, working, write.Operation.Origin, write.Operation.ClientId, cancellationToken);

                await hooks.RunAsync(HookKind.AfterCreate, write.Type, write.Operation, working, null, cancellationToken);
                _pending.Add(new EntryChange(EntryChangeKind.DraftSaved, write.Type.Name, id, document.Locale, document.DraftVersion, write.Operation));
                return document;
            },
            cancellationToken);

        return await RespondAsync(write, row, cancellationToken);
    }

    public Task<ContentDocument> UpdateAsync(string type, Guid id, ContentDocument entry, int expectedVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(entry);

        return SaveAsync(type, id, entry, expectedVersion, isPatch: false, cancellationToken);
    }

    public Task<ContentDocument> PatchAsync(string type, Guid id, ContentDocument patch, int expectedVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(patch);

        return SaveAsync(type, id, patch, expectedVersion, isPatch: true, cancellationToken);
    }

    public async Task DeleteAsync(string type, Guid id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);

        var write = await BeginAsync(type, ContentOperation.Delete, cancellationToken);
        await InTransactionAsync(
            async () =>
            {
                var (row, stored) = await ReachAllLocalesAsync(write, ContentOperation.Delete, id, trashed: false, cancellationToken);

                var working = ContentCopy.Of(stored);
                await hooks.RunAsync(HookKind.BeforeDelete, write.Type, write.Operation, working, stored, cancellationToken);
                await store.SetTrashedAsync(id, write.Operation.Timestamp, write.Operation.Principal.Id, cancellationToken);
                await hooks.RunAsync(HookKind.AfterDelete, write.Type, write.Operation, working, stored, cancellationToken);

                _pending.Add(new EntryChange(EntryChangeKind.Trashed, write.Type.Name, id, row.Locale, row.DraftVersion, write.Operation));
                return row;
            },
            cancellationToken);
    }

    public async Task<ContentDocument> RestoreAsync(string type, Guid id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);

        var write = await BeginAsync(type, ContentOperation.Restore, cancellationToken);
        var restored = await InTransactionAsync(
            async () =>
            {
                var (row, stored) = await ReachAllLocalesAsync(write, ContentOperation.Restore, id, trashed: true, cancellationToken);

                var working = ContentCopy.Of(stored);
                await hooks.RunAsync(HookKind.BeforeRestore, write.Type, write.Operation, working, stored, cancellationToken);
                await store.SetTrashedAsync(id, null, null, cancellationToken);
                await hooks.RunAsync(HookKind.AfterRestore, write.Type, write.Operation, working, stored, cancellationToken);

                _pending.Add(new EntryChange(EntryChangeKind.Restored, write.Type.Name, id, row.Locale, row.DraftVersion, write.Operation));
                return row;
            },
            cancellationToken);

        return await RespondAsync(write, restored, cancellationToken);
    }

    // No ambient operation is not an anonymous one: it is a bug in the entry point, and nothing runs. A client's origin
    // never runs as the system principal, whatever set it.
    private OperationContext Current()
    {
        var operation = accessor.Current
            ?? throw new InvalidOperationException("The content service runs inside an operation; the entry point sets IOperationContextAccessor.Current.");

        if (operation.Principal.IsSystem && !SystemOperation.Allows(operation.Origin))
        {
            throw new InvalidOperationException($"The system principal cannot come from the origin {operation.Origin}.");
        }

        return operation;
    }

    private async Task<string> ResolveLocaleAsync(SiteId site, string? locale, CancellationToken cancellationToken) =>
        await locales.ResolveAsync(new LocaleRequest(site) { Parameter = locale }, cancellationToken);

    // Reads ask the engine, which holds the permission on the type, the row rule, the site and the field limits as one query.
    private async Task<PagedResult<ContentDocument>> ReadAsync(string typeName, ContentQuery query, Guid? id, bool everySystemField, CancellationToken cancellationToken)
    {
        richText.ThrowIfUnknown(query.RichTextFormat);

        var operation = Current();
        var site = query.Site ?? operation.Site;
        var locale = await ResolveLocaleAsync(site, query.Locale ?? operation.Locale, cancellationToken);
        var everyLocale = locale == ILocaleResolver.All;
        var oneLocale = everyLocale ? await ResolveLocaleAsync(site, null, cancellationToken) : locale;

        var bound = query with { Site = site, Locale = oneLocale };
        IReadOnlyList<ContentDocument> found = [];
        ContentQueryResult? page = null;
        if (!everyLocale || id is null)
        {
            page = await engine.ExecuteAsync(typeName, bound, everySystemField, cancellationToken);
            found = page.Items;
        }

        // The type is known here, or the engine refused it; a read of every locale by id has not asked the engine yet.
        var type = schema.FindContentType(typeName)
            ?? throw new ValidationException([new ValidationError("type", QueryErrorCodes.UnknownField, $"'{typeName}' is not a content type that can be read.")]);

        var access = new FieldAccess(await evaluator.ResolveAsync(operation, cancellationToken), schema);
        if (everyLocale)
        {
            var ids = id is { } single ? [single] : found.Select(entry => (Guid)entry["id"]!).ToList();
            var items = await ReadEveryLocaleAsync(type, bound, ids, site, operation, access, cancellationToken);
            return new PagedResult<ContentDocument>(items, page?.Total, page?.Page, page?.PageSize ?? 1, page?.NextCursor);
        }

        var target = new AccessTarget(type.Scope == ContentScope.Global ? operation.Site : site, locale);
        foreach (var entry in found)
        {
            access.OmitUnreadable(entry, type, target);
        }

        if (type.Localized)
        {
            found = await ApplyFallbackAsync(type, bound, found, id, site, locale, access, target, cancellationToken);
        }

        if (!ResponseRichText.IsJson(query.RichTextFormat))
        {
            foreach (var entry in found)
            {
                richText.Apply(type, entry, query.RichTextFormat!);
            }
        }

        return new PagedResult<ContentDocument>(found, page?.Total, page?.Page, page?.PageSize ?? found.Count, page?.NextCursor);
    }

    private async Task<IReadOnlyList<ContentDocument>> ApplyFallbackAsync(SchemaType type, ContentQuery query, IReadOnlyList<ContentDocument> found, Guid? id, SiteId site, string locale, FieldAccess access, AccessTarget target, CancellationToken cancellationToken)
    {
        var chain = await fallback.ChainAsync(site, locale, query.Fallback, cancellationToken);
        if (found.Count > 0 || id is not { } single || query.Fallback == LocaleFallback.None)
        {
            await fallback.ApplyAsync(type.Name, query, found, chain, cancellationToken);
            return found;
        }

        // An entry read by id that has no row in this locale is served by the first locale of the chain that has one,
        // and every localized field it holds then counts as fallen back. A list does not do this: decision 44.
        if (await fallback.ReadFromChainAsync(type.Name, query, single, chain, cancellationToken) is not { } served)
        {
            return [];
        }

        // Read under the locale that served it, and limited as well by what the requested locale lets the principal read.
        served.Remove(LocaleFallbackReader.FallbacksKey, out var map);
        access.OmitUnreadable(served, type, target);
        if (map is ContentDocument fields)
        {
            foreach (var name in fields.Keys.Where(name => !served.ContainsKey(name)).ToList())
            {
                fields.Remove(name);
            }

            served[LocaleFallbackReader.FallbacksKey] = fields;
        }

        return [served];
    }

    private async Task<List<ContentDocument>> ReadEveryLocaleAsync(SchemaType type, ContentQuery query, IReadOnlyList<Guid> ids, SiteId site, OperationContext operation, FieldAccess access, CancellationToken cancellationToken)
    {
        var byEntry = await fallback.ReadAllAsync(type.Name, query with { Fallback = LocaleFallback.None }, ids, site, cancellationToken);
        List<ContentDocument> items = [];
        foreach (var id in ids)
        {
            var map = byEntry[id];
            foreach (var (code, value) in map)
            {
                var entry = (ContentDocument)value!;
                access.OmitUnreadable(entry, type, new AccessTarget(type.Scope == ContentScope.Global ? operation.Site : site, code));
                if (!ResponseRichText.IsJson(query.RichTextFormat))
                {
                    richText.Apply(type, entry, query.RichTextFormat!);
                }
            }

            if (map.Count > 0)
            {
                items.Add(new ContentDocument { ["id"] = id, ["locales"] = map });
            }
        }

        return items;
    }

    private async Task<WriteScope> BeginAsync(string typeName, ContentOperation operationKind, CancellationToken cancellationToken)
    {
        var operation = Current();
        var type = schema.FindContentType(typeName)
            ?? throw new ValidationException([new ValidationError("type", QueryErrorCodes.UnknownField, $"'{typeName}' is not a content type.")]);

        var locale = await ResolveLocaleAsync(operation.Site, operation.Locale, cancellationToken);
        if (locale == ILocaleResolver.All)
        {
            throw new ValidationException([new ValidationError("locale", QueryErrorCodes.InvalidParameter, "A write needs one locale.")]);
        }

        var target = new AccessTarget(operation.Site, locale);
        var permissions = await evaluator.ResolveAsync(operation, cancellationToken);
        if (!permissions.Allows(type, operationKind, target))
        {
            throw new ContentAccessDeniedException(type.Name, CorePermissions.OperationName(operationKind));
        }

        return new WriteScope(operation, type, operation.Site, locale, target, permissions, new FieldAccess(permissions, schema));
    }

    private async Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is not null)
        {
            return await work();
        }

        // On SQLite this takes the write lock at once, so a second writer waits here and then sees the first one's version.
        var committed = false;
        T result;
        var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await using (transaction)
        {
            try
            {
                result = await work();
                await transaction.CommitAsync(cancellationToken);
                committed = true;
            }
            finally
            {
                if (!committed)
                {
                    context.ChangeTracker.Clear();
                    _pending.Clear();
                }
            }
        }

        List<EntryChange> changes = [.. _pending];
        _pending.Clear();
        foreach (var change in changes)
        {
            foreach (var sink in sinks)
            {
                await sink.PublishAsync(change, cancellationToken);
            }
        }

        return result;
    }

    private async Task<ContentDocument> SaveAsync(string typeName, Guid id, ContentDocument changes, int expectedVersion, bool isPatch, CancellationToken cancellationToken)
    {
        var write = await BeginAsync(typeName, ContentOperation.Update, cancellationToken);
        var type = write.Type;
        var row = await InTransactionAsync(
            async () =>
            {
                var documents = await store.DocumentsAsync(type, id, write.Site, trashed: false, cancellationToken);
                if (documents.Count == 0)
                {
                    throw new EntryNotFoundException(type.Name, id);
                }

                // A locale the entry does not have yet is read against the locale it does have, and belongs to the same owner.
                var existing = documents.Find(item => item.Locale == write.DocumentLocale);
                var addsLocale = existing is null;
                var target = addsLocale ? write.Target with { RowLocale = documents[0].Locale } : write.Target;

                if (!await evaluator.CanAccessEntryAsync(write.Operation, type, ContentOperation.Update, id, target, QueryStatus.Draft, cancellationToken))
                {
                    throw new EntryNotFoundException(type.Name, id);
                }

                var document = existing ?? new Document
                {
                    DocumentId = id,
                    Locale = write.DocumentLocale,
                    Type = type.Name,
                    SiteId = documents[0].SiteId,
                    Status = EntryStatus.Draft,
                    DraftVersion = 0,
                    CreatedAt = write.Operation.Timestamp,
                    CreatedBy = documents[0].CreatedBy,
                    UpdatedAt = write.Operation.Timestamp,
                    UpdatedBy = write.Operation.Principal.Id,
                };

                if (document.DraftVersion != expectedVersion)
                {
                    throw await ConflictAsync(write, id, expectedVersion, document.DraftVersion, cancellationToken);
                }

                var writable = await evaluator.WritableFieldsAsync(write.Operation, type, ContentOperation.Update, id, target, cancellationToken);
                var stored = await store.ReadAsync(type, document, ContentState.Draft, cancellationToken);
                var refused = isPatch ? changes : WithoutUnchanged(write, changes, stored, writable);
                Reject(write, refused, writable);

                var composed = isPatch
                    ? composer.Patch(type, stored, changes)
                    : EntryComposer.Replace(type, stored, refused, field => Keeps(write, field, writable, addsLocale));
                var working = Prepare(write, composed, document);
                composer.CarryHidden(type, stored, working, includingGroups: !isPatch);

                working = await RunBeforeHooksAsync(write, HookKind.BeforeUpdate, working, stored, writable, document, cancellationToken);

                var sharedChanged = type.Localized && type.Fields.Any(field => !field.Localizable && !EntryJson.Same(working[field.Name], stored[field.Name]));
                var next = expectedVersion + 1;
                var status = document.Status == EntryStatus.Published ? EntryStatus.Modified : document.Status;
                if (addsLocale)
                {
                    document.DraftVersion = next;
                    store.AddDocument(document);
                }
                else if (await store.AdvanceVersionAsync(id, document.Locale, expectedVersion, status, write.Operation.Timestamp, write.Operation.Principal.Id, cancellationToken))
                {
                    document.DraftVersion = next;
                    document.Status = status;
                    document.UpdatedAt = write.Operation.Timestamp;
                    document.UpdatedBy = write.Operation.Principal.Id;
                }
                else
                {
                    throw await ConflictAsync(write, id, expectedVersion, await store.DraftVersionAsync(id, document.Locale, cancellationToken), cancellationToken);
                }

                await store.WriteDraftAsync(type, document, working, write.Operation.Origin, write.Operation.ClientId, cancellationToken);
                if (sharedChanged)
                {
                    await store.MarkOtherLocalesModifiedAsync(id, document.Locale, cancellationToken);
                }

                // A write may not take an entry out of the reach of the rule that let the principal change it.
                if (!await evaluator.CanAccessEntryAsync(write.Operation, type, ContentOperation.Update, id, write.Target, QueryStatus.Draft, cancellationToken))
                {
                    throw new ContentAccessDeniedException(type.Name, CorePermissions.OperationName(ContentOperation.Update));
                }

                EntryStore.ApplySystem(working, document);
                await hooks.RunAsync(HookKind.AfterUpdate, type, write.Operation, working, stored, cancellationToken);
                _pending.Add(new EntryChange(EntryChangeKind.DraftSaved, type.Name, id, document.Locale, document.DraftVersion, write.Operation));
                return document;
            },
            cancellationToken);

        return await RespondAsync(write, row, cancellationToken);
    }

    // A full update sends what a read gave it: a field the principal can see but not change is no write as long as it
    // is the same. Not for what it cannot see, because the answer would tell it the value: so the comparison leaves out
    // hidden values inside the field, and a value that names one is never "the same".
    private ContentDocument WithoutUnchanged(WriteScope write, ContentDocument changes, ContentDocument stored, WritableFields writable)
    {
        ContentDocument result = [];
        foreach (var (key, value) in changes)
        {
            var unchanged = write.Type.FindField(key) is { Hidden: false } field
                && (field.ReadOnly || !writable.Includes(key))
                && write.Permissions.CanReadField(write.Type, key, write.Target)
                && write.Access.IsClean(field, value, write.Type)
                && canonicalizer.TryRead(field, value, out var read)
                && JsonNode.DeepEquals(Visible(field, read), Visible(field, stored[key]));

            if (!unchanged)
            {
                result[key] = value;
            }
        }

        return result;
    }

    private JsonNode? Visible(SchemaField field, object? value)
    {
        var node = EntryJson.ToNode(value);
        HiddenFields.Remove(schema, node, field);
        return node;
    }

    private static bool Keeps(WriteScope write, SchemaField field, WritableFields writable, bool addsLocale) =>
        field.Hidden
        || field.ReadOnly
        || !writable.Includes(field.Name)
        || !write.Permissions.CanReadField(write.Type, field.Name, write.Target)
        || (addsLocale && !field.Localizable);

    private static void Reject(WriteScope write, ContentDocument changes, WritableFields writable)
    {
        try
        {
            write.Access.RejectUnwritable(changes, write.Type, writable);
        }
        catch (ArgumentException exception)
        {
            // JSON that repeats a key cannot be read, and so cannot be shown to stay within the limits.
            throw new ValidationException([new ValidationError("", ValidationErrorCodes.InvalidValue, exception.Message)]);
        }
    }

    private ContentDocument Prepare(WriteScope write, ContentDocument composed, Document document)
    {
        var working = canonicalizer.Canonicalize(write.Type, composed);
        ContentDocument withSystem = [];
        EntryStore.ApplySystem(withSystem, document);
        foreach (var (key, value) in working)
        {
            if (SystemField.Find(key) is null)
            {
                withSystem[key] = value;
            }
        }

        return withSystem;
    }

    // Validation runs on what hooks left, and again if BeforeCreate or BeforeUpdate changed it, so that a hook cannot get
    // a value past it. A hook is held to the principal's field limits as well, except on a hidden or read-only field,
    // which no client can reach and which is what a hook is for.
    private async Task<ContentDocument> RunBeforeHooksAsync(WriteScope write, HookKind before, ContentDocument working, ContentDocument? previous, WritableFields writable, Document document, CancellationToken cancellationToken)
    {
        var baseline = EntryJson.Fields(write.Type, working);
        await hooks.RunAsync(HookKind.BeforeValidate, write.Type, write.Operation, working, previous, cancellationToken);
        working = AfterHook(write, working, baseline, writable, document);
        await ValidateAsync(write, working, document.DocumentId, cancellationToken);

        var validated = EntryJson.Fields(write.Type, working);
        await hooks.RunAsync(before, write.Type, write.Operation, working, previous, cancellationToken);
        working = AfterHook(write, working, validated, writable, document);

        if (!JsonNode.DeepEquals(validated, EntryJson.Fields(write.Type, working)))
        {
            await ValidateAsync(write, working, document.DocumentId, cancellationToken);
        }

        return working;
    }

    private ContentDocument AfterHook(WriteScope write, ContentDocument working, JsonObject baseline, WritableFields writable, Document document)
    {
        var after = Prepare(write, working, document);
        var fields = EntryJson.Fields(write.Type, after);
        write.Access.RejectHookChanges(write.Type.Fields.Select(field => field.Name).Where(name => !JsonNode.DeepEquals(baseline[name], fields[name])), write.Type, writable);
        return after;
    }

    private async Task ValidateAsync(WriteScope write, ContentDocument working, Guid id, CancellationToken cancellationToken)
    {
        var entry = write.Type.ClrType is { } clrType ? mapper.ToEntry(clrType, working) : null;
        var result = await validation.ValidateAsync(
            write.Type,
            mapper.ValidationValues(write.Type, working),
            entry,
            write.Operation,
            ValidationMode.Draft,
            cancellationToken,
            (context, token) => new ValueTask(store.CheckUniqueAsync(write.Type, working, id, write.Site, write.Locale, context, token)));

        if (!result.IsValid)
        {
            throw new ValidationException(result.Errors);
        }
    }

    // One entry is one document: it goes to the trash and comes back in every locale, so the principal must hold the
    // operation on all of them. No row that it may reach reads as no entry, which keeps a client from probing.
    private async Task<(Document Row, ContentDocument Stored)> ReachAllLocalesAsync(WriteScope write, ContentOperation operation, Guid id, bool trashed, CancellationToken cancellationToken)
    {
        var documents = await store.DocumentsAsync(write.Type, id, write.Site, trashed, cancellationToken);
        var reached = 0;
        foreach (var document in documents)
        {
            var target = new AccessTarget(write.Site, write.Type.Localized ? document.Locale : write.Locale);
            if (await evaluator.CanAccessEntryAsync(write.Operation, write.Type, operation, id, target, QueryStatus.Draft, cancellationToken))
            {
                reached++;
            }
        }

        if (reached == 0)
        {
            throw new EntryNotFoundException(write.Type.Name, id);
        }

        if (reached < documents.Count)
        {
            throw new ContentAccessDeniedException(write.Type.Name, CorePermissions.OperationName(operation));
        }

        var row = documents.Find(document => document.Locale == write.DocumentLocale) ?? documents[0];
        return (row, await store.ReadAsync(write.Type, row, ContentState.Draft, cancellationToken));
    }

    private async Task<VersionConflictException> ConflictAsync(WriteScope write, Guid id, int expected, int current, CancellationToken cancellationToken) =>
        new(expected, current, await ReadDraftOrNullAsync(write, id, cancellationToken));

    // A principal may hold a write without the read. It still gets the id and the version, which it needs for its next write.
    private async Task<ContentDocument> RespondAsync(WriteScope write, Document row, CancellationToken cancellationToken) =>
        await ReadDraftOrNullAsync(write, row.DocumentId, cancellationToken)
            ?? new ContentDocument { ["id"] = row.DocumentId, ["version"] = row.DraftVersion };

    private async Task<ContentDocument?> ReadDraftOrNullAsync(WriteScope write, Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await GetAsync(write.Type.Name, id, new GetOptions { Status = QueryStatus.Draft, Locale = write.Locale, Fallback = LocaleFallback.None }, everySystemField: false, cancellationToken);
        }
        catch (ValidationException exception) when (exception.Errors.All(error => error.Path is "type" or "status"))
        {
            return null;
        }
    }

    private sealed record WriteScope(OperationContext Operation, SchemaType Type, SiteId Site, string Locale, AccessTarget Target, EffectivePermissions Permissions, FieldAccess Access)
    {
        public string DocumentLocale => Type.Localized ? Locale : "";

        public Guid? DocumentSite => Type.Scope == ContentScope.Global ? null : Site.Value;
    }
}
