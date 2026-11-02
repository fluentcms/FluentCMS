using System.Globalization;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content.Hooks;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Content.Validation;
using FluentCMS.Core.Events;
using FluentCMS.Core.Localization;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Localization;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

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
    IEnumerable<IEntryChangeSink> sinks,
    IEnumerable<IEntryChangeStager> stagers,
    EventBuffer events,
    ILogger<ContentService> logger) : IContentService
{
    private readonly List<EntryChange> _pending = [];
    private bool _ownsTransaction;
    private int _savepoints;

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
                var sent = WithoutFallbacks(entry);
                var writable = await evaluator.WritableFieldsAsync(write.Operation, write.Type, ContentOperation.Create, null, write.Target, cancellationToken);
                Reject(write, sent, writable);

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
                    CreatedBy = write.Operation.AuditPrincipal.Id,
                    UpdatedAt = write.Operation.Timestamp,
                    UpdatedBy = write.Operation.AuditPrincipal.Id,
                };

                ContentDocument composed = new(sent);
                foreach (var field in write.Type.Fields)
                {
                    if (field.DefaultValue is { } value && !composed.ContainsKey(field.Name))
                    {
                        composed[field.Name] = value;
                    }
                }

                var working = Prepare(write, composed, document, null);
                var prepared = EntryJson.Fields(write.Type, working);
                working = await RunBeforeHooksAsync(write, HookKind.BeforeCreate, working, new SaveContext(null, Client(sent), writable, document), cancellationToken);

                await store.WriteDraftAsync(write.Type, document, isNewDocument: true, working, write.Operation.Origin, write.Operation.ClientId, cancellationToken);

                // A row rule on a create grant has no entry to read until now. Each grant then answers for its own
                // fields (spec 4.8): what was written must be allowed by a grant whose rule holds for this entry.
                if (!await evaluator.CanAccessEntryAsync(write.Operation, write.Type, ContentOperation.Create, id, write.Target, QueryStatus.Draft, cancellationToken))
                {
                    throw new ContentAccessDeniedException(write.Type.Name, CorePermissions.OperationName(ContentOperation.Create));
                }

                var allowed = await evaluator.WritableFieldsAsync(write.Operation, write.Type, ContentOperation.Create, id, write.Target, cancellationToken);
                Reject(write, sent, allowed);
                var fields = EntryJson.Fields(write.Type, working);
                write.Access.RejectHookChanges(write.Type.Fields.Select(field => field.Name).Where(name => !JsonNode.DeepEquals(prepared[name], fields[name])), write.Type, allowed);

                await hooks.RunAsync(HookKind.AfterCreate, write.Type, write.Operation, working, null, cancellationToken);
                _pending.Add(new EntryChange(EntryChangeKind.DraftSaved, write.Type.Name, id, document.Locale, document.DraftVersion, write.Operation));
                return document;
            },
            cancellationToken);

        return await RespondAsync(write, row);
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
                var (documents, row, stored) = await ReachAllLocalesAsync(write, ContentOperation.Delete, id, trashed: false, cancellationToken);

                var working = ContentCopy.Of(stored);
                await hooks.RunAsync(HookKind.BeforeDelete, write.Type, write.Operation, working, ContentCopy.Of(stored), cancellationToken);
                await store.SetTrashedAsync(id, write.Operation.Timestamp, write.Operation.AuditPrincipal.Id, cancellationToken);
                await hooks.RunAsync(HookKind.AfterDelete, write.Type, write.Operation, working, ContentCopy.Of(stored), cancellationToken);

                _pending.AddRange(documents.Select(document => new EntryChange(EntryChangeKind.Trashed, write.Type.Name, id, document.Locale, document.DraftVersion, write.Operation)));
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
                var (documents, row, stored) = await ReachAllLocalesAsync(write, ContentOperation.Restore, id, trashed: true, cancellationToken);

                var working = ContentCopy.Of(stored);
                await hooks.RunAsync(HookKind.BeforeRestore, write.Type, write.Operation, working, ContentCopy.Of(stored), cancellationToken);
                await store.SetTrashedAsync(id, null, null, cancellationToken);
                await hooks.RunAsync(HookKind.AfterRestore, write.Type, write.Operation, working, ContentCopy.Of(stored), cancellationToken);

                _pending.AddRange(documents.Select(document => new EntryChange(EntryChangeKind.Restored, write.Type.Name, id, document.Locale, document.DraftVersion, write.Operation)));
                return row;
            },
            cancellationToken);

        return await RespondAsync(write, restored);
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

        // The entry point resolves the site before the operation (spec 4.6), and an API key's binding comes first, so a
        // query cannot name another one. The grants are read against the site named, which is why this comes first.
        var operation = Current();
        if (query.Site is { } asked && asked != operation.Site && !operation.Principal.IsSystem)
        {
            throw new ValidationException([new ValidationError("site", QueryErrorCodes.InvalidParameter, "A query reads the site of its operation.")]);
        }

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

    // What a typed write may name: a class holds a default for whatever the principal could not read.
    internal async Task<(Func<SchemaField, bool> CanRead, bool IsSystem)> TypedWriteLimitsAsync(string typeName, CancellationToken cancellationToken)
    {
        var operation = Current();
        var type = schema.FindContentType(typeName)
            ?? throw new ValidationException([new ValidationError("type", QueryErrorCodes.UnknownField, $"'{typeName}' is not a content type.")]);

        var target = new AccessTarget(operation.Site, await ResolveLocaleAsync(operation.Site, operation.Locale, cancellationToken));
        var permissions = await evaluator.ResolveAsync(operation, cancellationToken);
        return (field => permissions.CanReadField(type, field.Name, target), permissions.IsSystem);
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

    // Runs writes as one transaction and tells the sink what committed. A write inside another transaction takes a
    // savepoint, so that one that fails leaves nothing behind for that transaction to commit. Internal for #2703.
    internal async Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is { } outer)
        {
            return await InSavepointAsync(outer, work, cancellationToken);
        }

        // On SQLite this takes the write lock at once, so a second writer waits here and then sees the first one's version.
        var committed = false;
        T result;
        var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        _ownsTransaction = true;
        events.Open();
        await using (transaction)
        {
            try
            {
                result = await work();
                await StageAsync([.. _pending], cancellationToken);
                await transaction.CommitAsync(CancellationToken.None);
                committed = true;
            }
            finally
            {
                _ownsTransaction = false;
                events.Close();
                if (!committed)
                {
                    _pending.Clear();
                    events.Clear();
                }
            }
        }

        await PublishCommittedAsync();
        return result;
    }

    private async Task<T> InSavepointAsync<T>(IDbContextTransaction outer, Func<Task<T>> work, CancellationToken cancellationToken)
    {
        var pending = _pending.Count;
        var buffered = events.Mark;
        var savepoint = string.Create(CultureInfo.InvariantCulture, $"cms_content_{++_savepoints}");
        if (outer.SupportsSavepoints)
        {
            await outer.CreateSavepointAsync(savepoint, cancellationToken);
        }

        T result;
        try
        {
            result = await work();

            // The end of a transaction someone else began cannot be seen, so in-process handlers are never told. The rows of
            // the outbox handlers go in with the transaction, and commit or roll back with it.
            if (!_ownsTransaction)
            {
                await StageAsync([.. _pending.Skip(pending)], cancellationToken);
            }
        }
        catch
        {
            _pending.RemoveRange(pending, _pending.Count - pending);
            events.Truncate(buffered);

            // Without a savepoint the failed write's rows cannot be undone alone, and the transaction must not be
            // able to commit them, so all of it goes. SQL Server with MARS reports none: #2711.
            if (outer.SupportsSavepoints)
            {
                await outer.RollbackToSavepointAsync(savepoint, CancellationToken.None);
            }
            else
            {
                await outer.RollbackAsync(CancellationToken.None);
            }

            throw;
        }

        if (outer.SupportsSavepoints)
        {
            await outer.ReleaseSavepointAsync(savepoint, CancellationToken.None);
        }

        if (!_ownsTransaction)
        {
            _pending.RemoveRange(pending, _pending.Count - pending);
            events.Truncate(buffered);
        }

        return result;
    }

    // Still inside the transaction: what the outbox handlers must not lose is written with the write, and a failure here fails it.
    private async Task StageAsync(IReadOnlyList<EntryChange> changes, CancellationToken cancellationToken)
    {
        foreach (var stager in stagers)
        {
            await stager.StageAsync(context, changes, cancellationToken);
        }

        if (_ownsTransaction)
        {
            await events.StageAsync(context, cancellationToken);
        }
    }

    // The write has committed: neither a failing sink nor a cancelled caller may turn it into a failure (spec 4.11).
    private async Task PublishCommittedAsync()
    {
        List<EntryChange> changes = [.. _pending];
        _pending.Clear();
        foreach (var change in changes)
        {
            foreach (var sink in sinks)
            {
                try
                {
                    await sink.PublishAsync(change, CancellationToken.None);
                }
                catch (Exception exception)
                {
                    ContentLog.SinkFailed(logger, exception, change.Kind, change.Type, change.Id, change.Locale);
                }
            }
        }

        try
        {
            await events.DispatchAsync();
        }
        catch (Exception exception)
        {
            ContentLog.EventsFailed(logger, exception);
        }
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
                target = target with { EntryLocales = [.. documents.Select(item => item.Locale).Append(write.DocumentLocale).Distinct(StringComparer.OrdinalIgnoreCase)] };

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
                    UpdatedBy = write.Operation.AuditPrincipal.Id,
                };

                if (document.DraftVersion != expectedVersion)
                {
                    throw await ConflictAsync(write, id, expectedVersion, document.DraftVersion, cancellationToken);
                }

                var writable = await evaluator.WritableFieldsAsync(write.Operation, type, ContentOperation.Update, id, target, cancellationToken);
                var stored = await store.ReadAsync(type, document, ContentState.Draft, cancellationToken);
                var sent = WithoutFallbacks(changes);
                if (!isPatch)
                {
                    sent = await WithoutFallbackEchoesAsync(write, id, sent, stored, cancellationToken);
                }

                // A write answers what a read showed, so what that read left out is not the client's to change.
                var shown = NeedsWhatARead(write, sent, isPatch) ? await ReadShownAsync(write, id, documents, addsLocale, cancellationToken) : null;
                var refused = isPatch ? sent : WithoutUnchanged(write, sent, shown, writable, addsLocale);
                Reject(write, refused, writable);

                var composed = isPatch
                    ? composer.Patch(type, stored, sent)
                    : EntryComposer.Replace(type, stored, refused, field => Keeps(write, field, writable, addsLocale));
                var working = Prepare(write, composed, document, stored);
                composer.CarryProtected(type, stored, working, includingGroups: !isPatch || !write.Permissions.IsSystem);
                await KeepUnreadableTargetsAsync(write, stored, shown, working, isPatch, cancellationToken);

                working = await RunBeforeHooksAsync(write, HookKind.BeforeUpdate, working, new SaveContext(stored, Client(refused), writable, document), cancellationToken);

                var sharedChanged = type.Localized && type.Fields.Any(field => !field.Localizable && !EntryJson.Same(working[field.Name], stored[field.Name]));
                var next = expectedVersion + 1;
                var status = document.Status == EntryStatus.Published ? EntryStatus.Modified : document.Status;
                if (addsLocale)
                {
                    document.DraftVersion = next;
                }
                else if (await store.AdvanceVersionAsync(id, document.Locale, expectedVersion, status, write.Operation.Timestamp, write.Operation.AuditPrincipal.Id, cancellationToken))
                {
                    document.DraftVersion = next;
                    document.Status = status;
                    document.UpdatedAt = write.Operation.Timestamp;
                    document.UpdatedBy = write.Operation.AuditPrincipal.Id;
                }
                else
                {
                    throw await ConflictAsync(write, id, expectedVersion, await store.DraftVersionAsync(id, document.Locale, cancellationToken), cancellationToken);
                }

                await store.WriteDraftAsync(type, document, isNewDocument: addsLocale, working, write.Operation.Origin, write.Operation.ClientId, cancellationToken);
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
                await hooks.RunAsync(HookKind.AfterUpdate, type, write.Operation, working, ContentCopy.Of(stored), cancellationToken);
                _pending.Add(new EntryChange(EntryChangeKind.DraftSaved, type.Name, id, document.Locale, document.DraftVersion, write.Operation));
                return document;
            },
            cancellationToken);

        return await RespondAsync(write, row);
    }

    // A full update sends what a read gave it: a field the principal can see but not change is no write as long as it
    // is the same. The comparison is with what that read showed, on this row and with the relation targets it may read,
    // so the answer tells it nothing it could not read anyway. A value that names a hidden field is never the same.
    private ContentDocument WithoutUnchanged(WriteScope write, ContentDocument changes, ContentDocument? shown, WritableFields writable, bool addsLocale)
    {
        ContentDocument result = [];
        foreach (var (key, value) in changes)
        {
            var unchanged = shown is not null
                && write.Type.FindField(key) is { Hidden: false } field
                && (field.ReadOnly || !writable.Includes(key))
                && (!addsLocale || !field.Localizable)
                && shown.TryGetValue(key, out var visible)
                && write.Access.IsClean(field, value, write.Type)
                && canonicalizer.TryRead(field, value, out var read)
                && canonicalizer.TryRead(field, visible, out var seen)
                && JsonNode.DeepEquals(Visible(field, read), Visible(field, seen));

            if (!unchanged)
            {
                result[key] = value;
            }
        }

        return result;
    }

    private bool NeedsWhatARead(WriteScope write, ContentDocument sent, bool isPatch) =>
        !write.Permissions.IsSystem
        && (!isPatch || FieldStorage.Of(schema, write.Type).Any(item => item.Kind == FieldStorageKind.Reference && item.Field.Multiple && sent.ContainsKey(QueryPaths.ApiPath(write.Type, item).Split('.')[0])));

    // A locale being added has no row to read, so this reads a locale it has for the shared fields. One that has since
    // been disabled cannot be read, and the next one may.
    private async Task<ContentDocument?> ReadShownAsync(WriteScope write, Guid id, List<Document> documents, bool addsLocale, CancellationToken cancellationToken)
    {
        foreach (var locale in addsLocale ? documents.Select(document => document.Locale) : [write.Locale])
        {
            if (await ReadDraftOrNullAsync(write, id, locale, LocaleFallback.None, cancellationToken) is { } shown)
            {
                return shown;
            }
        }

        return null;
    }

    // What a read gave comes back with the targets it may not read missing. They are not the client's to remove, so each
    // goes back where it was in a multiple relation, which leaves a list sent as it was read unchanged, and a single
    // relation sent empty keeps its target. A patch names what it changes, so an explicit null on a single one clears.
    // A target that no longer exists is nothing to keep, and the system principal reads everything and keeps nothing.
    private async Task KeepUnreadableTargetsAsync(WriteScope write, ContentDocument stored, ContentDocument? shown, ContentDocument working, bool isPatch, CancellationToken cancellationToken)
    {
        if (write.Permissions.IsSystem)
        {
            return;
        }

        ContentDocument seen = [];
        foreach (var field in write.Type.Fields)
        {
            if (shown is not null && shown.TryGetValue(field.Name, out var visible) && canonicalizer.TryRead(field, visible, out var read))
            {
                seen[field.Name] = read;
            }
        }

        foreach (var item in FieldStorage.Of(schema, write.Type).Where(item => item.Kind == FieldStorageKind.Reference && (!isPatch || item.Field.Multiple)))
        {
            var before = IdsOf(EntryPaths.ValueAt(stored, item, write.Type));
            var unseen = before.Except(IdsOf(EntryPaths.ValueAt(seen, item, write.Type))).ToList();
            if (unseen.Count == 0)
            {
                continue;
            }

            var kept = await store.ExistingTargetsAsync(item.Field, unseen, cancellationToken);
            var sent = IdsOf(EntryPaths.ValueAt(working, item, write.Type));
            if (item.Field.Multiple)
            {
                for (var index = 0; index < before.Count; index++)
                {
                    if (kept.Contains(before[index]) && !sent.Contains(before[index]))
                    {
                        sent.Insert(Math.Min(index, sent.Count), before[index]);
                    }
                }

                EntryPaths.SetAt(working, item, write.Type, sent.ToArray());
            }
            else if (sent.Count == 0 && kept.Count > 0)
            {
                EntryPaths.SetAt(working, item, write.Type, kept[0]);
            }
        }
    }

    private static List<Guid> IdsOf(object? value) => value switch
    {
        Guid id => [id],
        Guid[] ids => [.. ids],
        _ => [],
    };

    // What a read adds is no value to write.
    private static ContentDocument WithoutFallbacks(ContentDocument changes)
    {
        if (!changes.ContainsKey(LocaleFallbackReader.FallbacksKey))
        {
            return changes;
        }

        var result = new ContentDocument(changes);
        result.Remove(LocaleFallbackReader.FallbacksKey);
        return result;
    }

    // A full update sends back what a read gave it, and for a field empty in this locale that was another locale's
    // value. Sent unchanged it is not a translation, so it counts as not sent; a different value is an edit. A patch
    // names what it changes. The chain is only read when there is such a field.
    private async Task<ContentDocument> WithoutFallbackEchoesAsync(WriteScope write, Guid id, ContentDocument sent, ContentDocument stored, CancellationToken cancellationToken)
    {
        List<(SchemaField Field, object? Read)> candidates = [];
        foreach (var field in write.Type.Fields.Where(field => field.Localizable && !field.Hidden && sent.ContainsKey(field.Name)))
        {
            if (LocaleFallbackReader.IsMissing(stored.GetValueOrDefault(field.Name))
                && canonicalizer.TryRead(field, sent[field.Name], out var read)
                && !LocaleFallbackReader.IsMissing(read))
            {
                candidates.Add((field, read));
            }
        }

        if (candidates.Count == 0
            || await ReadDraftOrNullAsync(write, id, write.Locale, LocaleFallback.Chain, cancellationToken) is not { } served
            || served.GetValueOrDefault(LocaleFallbackReader.FallbacksKey) is not ContentDocument fallenBack)
        {
            return sent;
        }

        var result = new ContentDocument(sent);
        foreach (var (field, read) in candidates)
        {
            if (fallenBack.ContainsKey(field.Name)
                && canonicalizer.TryRead(field, served.GetValueOrDefault(field.Name), out var chain)
                && JsonNode.DeepEquals(Visible(field, read), Visible(field, chain)))
            {
                result.Remove(field.Name);
            }
        }

        return result;
    }

    private static JsonObject Client(ContentDocument sent) => (JsonObject)EntryJson.ToNode(sent)!;

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

    // A hidden field shows to no one, and the rest as the principal's grants say. A nested source goes with the field
    // that holds it, since an allow-list names top-level fields.
    private static bool MayDerive(WriteScope write, SchemaType owner, SchemaField source) =>
        !source.Hidden && (owner != write.Type || write.Permissions.CanReadField(write.Type, source.Name, write.Target));

    private ContentDocument Prepare(WriteScope write, ContentDocument composed, Document document, ContentDocument? stored)
    {
        var working = canonicalizer.Canonicalize(write.Type, composed, (owner, source) => MayDerive(write, owner, source), stored);
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
    // which no client can reach and which is what a hook is for. Hooks get a copy of the stored state, so they cannot
    // change what the write compares with.
    private async Task<ContentDocument> RunBeforeHooksAsync(WriteScope write, HookKind before, ContentDocument working, SaveContext save, CancellationToken cancellationToken)
    {
        var sent = EntryJson.Fields(write.Type, working);

        await hooks.RunAsync(HookKind.BeforeValidate, write.Type, write.Operation, working, CopyOf(save.Stored), cancellationToken);
        working = AfterHook(write, working, sent, save);
        await ValidateAsync(write, working, sent, save, cancellationToken);

        var validated = EntryJson.Fields(write.Type, working);
        await hooks.RunAsync(before, write.Type, write.Operation, working, CopyOf(save.Stored), cancellationToken);
        working = AfterHook(write, working, validated, save);

        if (!JsonNode.DeepEquals(validated, EntryJson.Fields(write.Type, working)))
        {
            await ValidateAsync(write, working, sent, save, cancellationToken);
        }

        return working;
    }

    private static ContentDocument? CopyOf(ContentDocument? stored) => stored is null ? null : ContentCopy.Of(stored);

    private ContentDocument AfterHook(WriteScope write, ContentDocument working, JsonObject baseline, SaveContext save)
    {
        var after = Prepare(write, working, save.Document, save.Stored);
        var fields = EntryJson.Fields(write.Type, after);
        write.Access.RejectHookChanges(write.Type.Fields.Select(field => field.Name).Where(name => !JsonNode.DeepEquals(baseline[name], fields[name])), write.Type, save.Writable);
        return after;
    }

    private async Task ValidateAsync(WriteScope write, ContentDocument working, JsonObject sent, SaveContext save, CancellationToken cancellationToken)
    {
        var id = save.Document.DocumentId;
        var entry = write.Type.ClrType is { } clrType ? mapper.ToEntry(clrType, working) : null;
        var values = mapper.ValidationValues(write.Type, working);
        var checkedUnique = false;
        var result = await validation.ValidateAsync(
            write.Type,
            values,
            entry,
            write.Operation,
            ValidationMode.Draft,
            cancellationToken,
            (context, token) =>
            {
                checkedUnique = true;
                return new ValueTask(store.CheckUniqueAsync(write.Type, working, id, write.Site, write.Locale, context, token));
            });

        var now = EntryJson.Fields(write.Type, working);
        var errors = await ReportableAsync(write, result.Errors, now, sent, save, cancellationToken);

        // The pipeline skips uniqueness after an error, and a dropped error is no reason to.
        if (errors.Count == 0 && !checkedUnique)
        {
            var context = new ValidationContext(write.Type, values, write.Operation, ValidationMode.Draft, entry);
            await store.CheckUniqueAsync(write.Type, working, id, write.Site, write.Locale, context, cancellationToken);
            errors = await ReportableAsync(write, context.Errors, now, sent, save, cancellationToken);
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    private async Task<List<ValidationError>> ReportableAsync(WriteScope write, IReadOnlyList<ValidationError> errors, JsonObject now, JsonObject sent, SaveContext save, CancellationToken cancellationToken)
    {
        var (reportable, toCompare, produced) = new EntryErrors(schema, write.Type, write.Permissions, write.Target).Sort(errors, now, sent, save.FromClient);
        if (!produced && toCompare.Count > 0)
        {
            var before = save.Stored is { } stored ? await ValidateStoredAsync(write, stored, save.Document.DocumentId, cancellationToken) : [];
            produced = toCompare.Any(error => !before.Any(old => old.Path == error.Path && old.Code == error.Code));
        }

        if (produced)
        {
            reportable.Add(EntryErrors.ProducedValue);
        }

        return reportable;
    }

    private async Task<List<ValidationError>> ValidateStoredAsync(WriteScope write, ContentDocument stored, Guid id, CancellationToken cancellationToken)
    {
        var entry = write.Type.ClrType is { } clrType ? mapper.ToEntry(clrType, stored) : null;
        var values = mapper.ValidationValues(write.Type, stored);
        var result = await validation.ValidateAsync(write.Type, values, entry, write.Operation, ValidationMode.Draft, cancellationToken);
        var unique = new ValidationContext(write.Type, values, write.Operation, ValidationMode.Draft, entry);
        await store.CheckUniqueAsync(write.Type, stored, id, write.Site, write.Locale, unique, cancellationToken);
        return [.. result.Errors, .. unique.Errors];
    }

    // One entry is one document: it goes to the trash and comes back in every locale, so the principal must hold the
    // operation on all of them. No row that it may reach reads as no entry, which keeps a client from probing.
    private async Task<(List<Document> Documents, Document Row, ContentDocument Stored)> ReachAllLocalesAsync(WriteScope write, ContentOperation operation, Guid id, bool trashed, CancellationToken cancellationToken)
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
        return (documents, row, await store.ReadAsync(write.Type, row, ContentState.Draft, cancellationToken));
    }

    private async Task<VersionConflictException> ConflictAsync(WriteScope write, Guid id, int expected, int current, CancellationToken cancellationToken) =>
        new(expected, current, await ReadDraftOrNullAsync(write, id, write.Locale, LocaleFallback.None, cancellationToken));

    // A principal may hold a write without the read. It still gets the id and the version, which it needs for its next
    // write. The write has committed by now, so a cancelled caller does not turn it into a failure.
    private async Task<ContentDocument> RespondAsync(WriteScope write, Document row) =>
        await ReadDraftOrNullAsync(write, row.DocumentId, write.Locale, LocaleFallback.None, CancellationToken.None)
            ?? new ContentDocument { ["id"] = row.DocumentId, ["version"] = row.DraftVersion };

    private async Task<ContentDocument?> ReadDraftOrNullAsync(WriteScope write, Guid id, string locale, LocaleFallback fallbacks, CancellationToken cancellationToken)
    {
        try
        {
            return await GetAsync(write.Type.Name, id, new GetOptions { Status = QueryStatus.Draft, Locale = string.IsNullOrEmpty(locale) ? write.Locale : locale, Fallback = fallbacks }, everySystemField: false, cancellationToken);
        }
        catch (ValidationException exception) when (exception.Errors.All(error => error.Path is "type" or "status" or "locale"))
        {
            return null;
        }
    }

    // What the steps of one save share.
    private sealed record SaveContext(ContentDocument? Stored, JsonObject FromClient, WritableFields Writable, Document Document);

    private sealed record WriteScope(OperationContext Operation, SchemaType Type, SiteId Site, string Locale, AccessTarget Target, EffectivePermissions Permissions, FieldAccess Access)
    {
        public string DocumentLocale => Type.Localized ? Locale : "";

        public Guid? DocumentSite => Type.Scope == ContentScope.Global ? null : Site.Value;
    }
}
