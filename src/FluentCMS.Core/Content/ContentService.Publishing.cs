using System.Globalization;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content.Hooks;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Content.Validation;
using FluentCMS.Core.Localization;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Hooks;
using FluentCMS.Publishing;
using FluentCMS.Schema;

namespace FluentCMS.Core.Content;

// The transitions of spec 4.5 and the versions behind them. Each is a write like any other, and what a version holds is
// read and restored as this principal may today, not as it was saved.
internal sealed partial class ContentService
{
    public Task<PublishResult<ContentDocument>> PublishAsync(string type, Guid id, LocaleWriteOptions? options = null, CancellationToken cancellationToken = default) =>
        PublishAsync(type, id, options?.Locale, options?.ExpectedVersion, cancellationToken);

    internal async Task<PublishResult<ContentDocument>> PublishAsync(string type, Guid id, string? locale, int? expectedVersion, CancellationToken cancellationToken)
    {
        using var span = Span("publish", type);

        ArgumentNullException.ThrowIfNull(type);

        var write = await BeginAsync(type, ContentOperation.Publish, cancellationToken, locale);
        List<PublishWarning> warnings = [];
        var ownsTransaction = context.Database.CurrentTransaction is null;
        var row = await InTransactionAsync(
            async () =>
            {
                var (documents, document, target) = await ReachLocaleAsync(write, ContentOperation.Publish, id, cancellationToken);
                await ThrowIfNotExpectedAsync(write, id, document, expectedVersion, cancellationToken);
                var read = document.DraftVersion;

                var (draft, rows) = await store.ReadDraftAsync(write.Type, document, cancellationToken);
                var published = await store.ReadAsync(write.Type, document, ContentState.Published, cancellationToken);
                var previous = document.PublishedVersion is null ? null : published;

                var writable = await CheckPublishGrantAsync(write, documents, document, target, draft, published, cancellationToken);

                // Settling what is settled changes nothing, so it is done again only after a hook that ran.
                var original = EntryJson.Fields(write.Type, draft);
                var working = ContentCopy.Of(draft);
                var movedBefore = _moved.Count;
                await hooks.RunAsync(HookKind.BeforeValidate, write.Type, write.Operation, working, CopyOf(previous), cancellationToken);
                (working, var validated) = Settle(write, working, original, draft, writable);
                await ValidateForPublishAsync(write, working, validated, document, target, warnings, cancellationToken);

                var settled = validated;
                if (await hooks.RunAsync(HookKind.BeforePublish, write.Type, write.Operation, working, CopyOf(previous), beforeFirst: null, cancellationToken))
                {
                    (working, settled) = Settle(write, working, validated, draft, writable);
                    if (!JsonNode.DeepEquals(validated, settled))
                    {
                        await ValidateForPublishAsync(write, working, settled, document, target, warnings, cancellationToken);
                    }
                }

                Task<Exception> LostAsync(string locale) => SwapLostAsync(write, id, locale, read, movedBefore, ownsTransaction, cancellationToken);

                // A hook can call the service. What it saved, in another locale too, would go out with the shared row unvalidated,
                // and so would what another request committed since the first read, which read committed shows here.
                if (!JsonNode.DeepEquals(original, EntryJson.Fields(write.Type, await store.ReadAsync(write.Type, document, ContentState.Draft, cancellationToken))))
                {
                    throw await LostAsync(document.Locale);
                }

                // Held before the copy, in the lock order (#3524): this row at the version read, and every other row of the
                // entry, since the copy of the shared row decides the status of each locale published by then, one published
                // while this publish runs too, and a write's own status is decided from that row once it holds its own.
                var locks = new HeldRows([.. documents.Where(other => other.Id != document.Id)], LostAsync);
                List<Document> advanced = [];
                if (!JsonNode.DeepEquals(original, settled))
                {
                    var status = document.Status == EntryStatus.Published ? EntryStatus.Modified : document.Status;
                    advanced = await WriteOverDraftAsync(write, id, documents, document, rows, draft, working, settled, status, null, cancellationToken, locks);
                    await ThrowIfOutOfReachAsync(write, ContentOperation.Publish, id, cancellationToken);
                }
                else
                {
                    await AdvanceVersionsAsync(write, id, document, read, status: null, [], cancellationToken, locks);
                }

                await store.CopyDraftToPublishedAsync(write.Type, document, cancellationToken);
                if (!await store.MarkPublishedAsync(document, write.Operation.Timestamp, write.Operation.AuditPrincipal.Id, cancellationToken))
                {
                    throw await LostAsync(document.Locale);
                }

                await RecomputeOtherLocalesAsync(write, document, cancellationToken, advanced);

                EntryStore.ApplySystem(working, document);
                await hooks.RunAsync(HookKind.AfterPublish, write.Type, write.Operation, working, CopyOf(previous), cancellationToken);
                _pending.Add(new EntryChange(EntryChangeKind.Published, write.Type.Name, id, document.Locale, document.DraftVersion, write.Operation));
                return document;
            },
            cancellationToken);

        return new PublishResult<ContentDocument>(await RespondAsync(write, row), warnings);
    }

    // The draft read again or a swap of the publish found a locale moved (#3536, #3546). A hook's save and another request's
    // both move the stored version, so that alone cannot name the writer. A swap of this transaction holds the row to its end,
    // so a locale this transaction moved since the publish began is a hook's, also when it saved the same values, and with
    // none a newer version is another's commit. A publish in a transaction of its own ends it before it reads that version and
    // entry, so that they are what is stored and nothing a hook wrote past the swaps; one inside another transaction cannot,
    // and reads them as that one sees them. Another locale that lost is a 412 with this locale's version, as for a save.
    private async Task<Exception> SwapLostAsync(WriteScope write, Guid id, string locale, int read, int movedBefore, bool ownsTransaction, CancellationToken cancellationToken)
    {
        // A write nested in a validator can have left a hook running, which refuses the publish before either answer (FCMS2410).
        hooks.ThrowIfLeftRunning();

        if (!_moved.Skip(movedBefore).Contains((id, locale)))
        {
            if (ownsTransaction)
            {
                await context.Database.RollbackTransactionAsync(CancellationToken.None);
            }

            // A row in the trash, or gone, is no hook's doing: the entry is not found, as a publish of it would answer.
            if (await store.LiveDraftVersionAsync(id, write.DocumentLocale, cancellationToken) is not { } stored)
            {
                return new EntryNotFoundException(write.Type.Name, id);
            }

            if (stored > read || locale != write.DocumentLocale)
            {
                return await ConflictAsync(write, id, read, stored, cancellationToken);
            }
        }

        return new HookRejectedException(ContentErrorCodes.DraftMovedByHook, "A hook changed the draft being published.");
    }

    // A swap of a save, a discard or a version restore lost to a locale this transaction moved since the write's hooks began
    // (#3568), as a publish's does (#3546): a hook or a validator saved the entry, and since the swap holds the row to the
    // transaction's end no other request moved it after. What it moved rolls back with the write, so a 412 would name a
    // version never committed. Null when the locale lost to another request.
    private HookRejectedException? MovedByHook(Guid id, string locale, int movedBefore)
    {
        if (!_moved.Skip(movedBefore).Contains((id, locale)))
        {
            return null;
        }

        hooks.ThrowIfLeftRunning();
        return new HookRejectedException(ContentErrorCodes.DraftMovedByHook, "A hook changed the draft being saved.");
    }

    // What a publish or a discard holds besides its own row, and what it answers when one of its swaps loses.
    private sealed record HeldRows(IReadOnlyCollection<Document> Held, Func<string, Task<Exception>> Lost);

    public Task<ContentDocument> UnpublishAsync(string type, Guid id, LocaleOptions? options = null, CancellationToken cancellationToken = default) =>
        UnpublishAsync(type, id, options?.Locale, cancellationToken);

    internal async Task<ContentDocument> UnpublishAsync(string type, Guid id, string? locale, CancellationToken cancellationToken)
    {
        using var span = Span("unpublish", type);

        ArgumentNullException.ThrowIfNull(type);

        var write = await BeginAsync(type, ContentOperation.Unpublish, cancellationToken, locale);
        var row = await InTransactionAsync(
            async () =>
            {
                var (documents, document, target) = await ReachLocaleAsync(write, ContentOperation.Unpublish, id, cancellationToken);
                ThrowIfNotPublished(write, document);

                await CheckUnpublishGrantAsync(write, document, target, cancellationToken);

                var published = await store.ReadAsync(write.Type, document, ContentState.Published, cancellationToken);
                var working = ContentCopy.Of(published);
                await hooks.RunAsync(HookKind.BeforeUnpublish, write.Type, write.Operation, working, ContentCopy.Of(published), cancellationToken);

                // Whether this locale is the last published decides whether the shared published row goes, so it is read
                // under the holds of every row of the entry, which a publish of another locale takes too (#3524).
                await HoldInOrderAsync(documents, cancellationToken);
                var isLast = !(await store.DocumentsAsync(write.Type, id, write.Site, trashed: false, cancellationToken)).Any(other => other.Id != document.Id && other.PublishedVersion is not null);
                if (!await store.MarkUnpublishedAsync(document, write.Operation.Timestamp, write.Operation.AuditPrincipal.Id, cancellationToken))
                {
                    throw await ConflictAsync(write, id, document.DraftVersion, await store.DraftVersionAsync(id, document.Locale, cancellationToken), cancellationToken);
                }

                await store.DeletePublishedAsync(write.Type, document, isLast, cancellationToken);

                await hooks.RunAsync(HookKind.AfterUnpublish, write.Type, write.Operation, working, ContentCopy.Of(published), cancellationToken);
                _pending.Add(new EntryChange(EntryChangeKind.Unpublished, write.Type.Name, id, document.Locale, document.DraftVersion, write.Operation));
                return document;
            },
            cancellationToken);

        return await RespondAsync(write, row);
    }

    public Task<ContentDocument> DiscardDraftAsync(string type, Guid id, LocaleWriteOptions? options = null, CancellationToken cancellationToken = default) =>
        DiscardDraftAsync(type, id, options?.Locale, options?.ExpectedVersion, cancellationToken);

    internal async Task<ContentDocument> DiscardDraftAsync(string type, Guid id, string? locale, int? expectedVersion, CancellationToken cancellationToken)
    {
        using var span = Span("discard-draft", type);

        ArgumentNullException.ThrowIfNull(type);

        var write = await BeginAsync(type, ContentOperation.Update, cancellationToken, locale);
        var row = await InTransactionAsync(
            async () =>
            {
                var (documents, document, target) = await ReachLocaleAsync(write, ContentOperation.Update, id, cancellationToken);
                ThrowIfNotPublished(write, document);
                await ThrowIfNotExpectedAsync(write, id, document, expectedVersion, cancellationToken);

                // A discard writes what is published, so before it reads that it holds every row of the entry, its own at the
                // version it read, as a publish does, and is a conflict when its row was published, unpublished or given another
                // status since (#3524).
                Task<Exception> LostAsync(string locale) => OwnRowLostAsync(write, id, document.DraftVersion, cancellationToken);
                var read = new StoredState(document.PublishedVersion, document.Status);
                await AdvanceVersionsAsync(write, id, document, document.DraftVersion, status: null, [], cancellationToken, new HeldRows([.. documents.Where(other => other.Id != document.Id)], LostAsync));
                if (await store.StateAsync(document, cancellationToken) != read)
                {
                    throw await ConflictAsync(write, id, document.DraftVersion, document.DraftVersion, cancellationToken);
                }

                var writable = await evaluator.WritableFieldsAsync(write.Operation, write.Type, ContentOperation.Update, id, target, cancellationToken);
                ThrowIfWritesBlind(write, writable);

                var (stored, rows) = await store.ReadDraftAsync(write.Type, document, cancellationToken);
                var published = await store.ReadAsync(write.Type, document, ContentState.Published, cancellationToken);
                var publishedFields = EntryJson.Fields(write.Type, published);
                if (JsonNode.DeepEquals(EntryJson.Fields(write.Type, stored), publishedFields))
                {
                    if (document.Status != EntryStatus.Published)
                    {
                        await store.SetStatusAsync(document, EntryStatus.Published, cancellationToken);
                    }

                    return document;
                }

                var working = Prepare(write, EntryComposer.Replace(write.Type, stored, DiscardedValues(write, published, writable), static _ => true), document, stored);
                if (writable.Fields is not null)
                {
                    composer.CarryProtected(write.Type, stored, working, includingGroups: true);
                }

                var movedBefore = _moved.Count;
                (working, var fields) = await RunBeforeHooksAsync(write, HookKind.BeforeUpdate, working, new SaveContext(stored, [], writable, document), cancellationToken);

                var advanced = await WriteOverDraftAsync(write, id, documents, document, rows, stored, working, fields, JsonNode.DeepEquals(fields, publishedFields) ? EntryStatus.Published : EntryStatus.Modified, "Discarded the draft.", cancellationToken, new HeldRows([], LostAsync), movedBefore);
                await RecomputeOtherLocalesAsync(write, document, cancellationToken, advanced);
                var skipped = await SkipStalePublishesAsync(write, documents, document, cancellationToken);
                await ThrowIfOutOfReachAsync(write, ContentOperation.Update, id, cancellationToken);

                EntryStore.ApplySystem(working, document);
                await hooks.RunAsync(HookKind.AfterUpdate, write.Type, write.Operation, working, ContentCopy.Of(stored), cancellationToken);
                _pending.Add(new EntryChange(EntryChangeKind.DraftDiscarded, write.Type.Name, id, document.Locale, document.DraftVersion, write.Operation));
                _pending.AddRange(skipped);
                return document;
            },
            cancellationToken);

        return await RespondAsync(write, row);
    }

    // Whole for a principal that may write every field. Otherwise as an update: only what it may write, without the hidden
    // and read-only values inside, which stay as the draft holds them.
    private ContentDocument DiscardedValues(WriteScope write, ContentDocument published, WritableFields writable)
    {
        ContentDocument values = [];
        foreach (var field in write.Type.Fields)
        {
            if (writable.Allowed && writable.Fields is null)
            {
                values[field.Name] = published.GetValueOrDefault(field.Name);
            }
            else if (!field.Hidden && !field.ReadOnly && writable.Includes(field.Name))
            {
                var value = EntryJson.ToNode(published.GetValueOrDefault(field.Name));
                HiddenFields.RemoveWhere(schema, value, field, static inner => inner.Hidden || inner.ReadOnly);
                values[field.Name] = value;
            }
        }

        return values;
    }

    public Task<IReadOnlyList<VersionInfo>> GetVersionsAsync(string type, Guid id, LocaleOptions? options = null, CancellationToken cancellationToken = default) =>
        GetVersionsAsync(type, id, options?.Locale, cancellationToken);

    internal async Task<IReadOnlyList<VersionInfo>> GetVersionsAsync(string type, Guid id, string? locale, CancellationToken cancellationToken)
    {
        using var span = Span("versions", type);

        ArgumentNullException.ThrowIfNull(type);

        var (_, document) = await BeginVersionReadAsync(type, id, locale, cancellationToken);
        return await store.VersionsAsync(id, document.Locale, cancellationToken);
    }

    public Task<ContentDocument?> GetVersionAsync(string type, Guid id, int number, LocaleOptions? options = null, CancellationToken cancellationToken = default) =>
        GetVersionAsync(type, id, number, options?.Locale, cancellationToken);

    internal async Task<ContentDocument?> GetVersionAsync(string type, Guid id, int number, string? locale, CancellationToken cancellationToken)
    {
        using var span = Span("version", type);

        ArgumentNullException.ThrowIfNull(type);

        var (write, document) = await BeginVersionReadAsync(type, id, locale, cancellationToken);
        if (await store.VersionAsync(id, document.Locale, number, cancellationToken) is not { } version)
        {
            return null;
        }

        var fields = await ReadableFieldsOfAsync(write, version, cancellationToken);
        ContentDocument entry = new()
        {
            ["id"] = id,
            ["status"] = version.StatusAfter,
            ["locale"] = version.Locale,
            ["version"] = version.Number,
            ["updatedAt"] = version.CreatedAt,
        };

        foreach (var (name, value) in fields)
        {
            if (write.Type.FindField(name) is { } field && canonicalizer.TryRead(field, value, out var read))
            {
                entry[name] = AsRead(field, read);
            }
        }

        write.Access.OmitUnreadable(entry, write.Type, write.Target);
        return entry;
    }

    public Task<ContentDocument> RestoreVersionAsync(string type, Guid id, int number, LocaleWriteOptions? options = null, CancellationToken cancellationToken = default) =>
        RestoreVersionAsync(type, id, number, options?.Locale, options?.ExpectedVersion, cancellationToken);

    internal async Task<ContentDocument> RestoreVersionAsync(string type, Guid id, int number, string? locale, int? expectedVersion, CancellationToken cancellationToken)
    {
        using var span = Span("restore-version", type);

        ArgumentNullException.ThrowIfNull(type);

        var write = await BeginAsync(type, ContentOperation.Update, cancellationToken, locale);
        var row = await InTransactionAsync(
            async () =>
            {
                var (documents, document, target) = await ReachLocaleAsync(write, ContentOperation.Update, id, cancellationToken);
                await ThrowIfNotExpectedAsync(write, id, document, expectedVersion, cancellationToken);
                var version = await store.VersionAsync(id, document.Locale, number, cancellationToken)
                    ?? throw new EntryNotFoundException(write.Type.Name, id);

                var writable = await evaluator.WritableFieldsAsync(write.Operation, write.Type, ContentOperation.Update, id, target, cancellationToken);
                ThrowIfWritesBlind(write, writable);

                var (stored, rows) = await store.ReadDraftAsync(write.Type, document, cancellationToken);
                var restored = await RestorableFieldsAsync(write, version, writable, cancellationToken);

                var isSystem = write.Permissions.IsSystem;
                var composed = EntryComposer.Replace(write.Type, stored, restored, field => !isSystem && Keeps(write, field, writable, addsLocale: false));
                var working = Prepare(write, composed, document, stored);
                if (!isSystem)
                {
                    var shown = await ReadShownAsync(write, id, documents, addsLocale: false, known: null, cancellationToken);
                    composer.CarryProtected(write.Type, stored, working, includingGroups: true, await UnseenNestedTargetsAsync(write, stored, shown, cancellationToken));
                    await KeepUnreadableTargetsAsync(write, stored, shown, working, isPatch: false, cancellationToken);
                }

                var movedBefore = _moved.Count;
                (working, var fields) = await RunBeforeHooksAsync(write, HookKind.BeforeUpdate, working, new SaveContext(stored, Client(restored), writable, document), cancellationToken);

                var status = document.Status;
                if (document.PublishedVersion is not null)
                {
                    var published = await store.ReadAsync(write.Type, document, ContentState.Published, cancellationToken);
                    status = JsonNode.DeepEquals(fields, EntryJson.Fields(write.Type, published)) ? EntryStatus.Published : EntryStatus.Modified;
                }

                var message = string.Create(CultureInfo.InvariantCulture, $"Restored version {number}.");
                var advanced = await WriteOverDraftAsync(write, id, documents, document, rows, stored, working, fields, status, message, cancellationToken, movedBefore: movedBefore);

                // Without a shared change neither the shared row nor another locale's draft moved, so no other status did, and
                // the rows of the other locales, which it does not hold, are left alone.
                if (advanced.Count > 0)
                {
                    await RecomputeOtherLocalesAsync(write, document, cancellationToken, advanced);
                }

                var skipped = await SkipStalePublishesAsync(write, documents, document, cancellationToken);
                await ThrowIfOutOfReachAsync(write, ContentOperation.Update, id, cancellationToken);

                EntryStore.ApplySystem(working, document);
                await hooks.RunAsync(HookKind.AfterUpdate, write.Type, write.Operation, working, ContentCopy.Of(stored), cancellationToken);
                _pending.Add(new EntryChange(EntryChangeKind.VersionRestored, write.Type.Name, id, document.Locale, document.DraftVersion, write.Operation));
                _pending.AddRange(skipped);
                return document;
            },
            cancellationToken);

        return await RespondAsync(write, row);
    }

    public Task<IReadOnlyList<VersionChange>> DiffAsync(string type, Guid id, int fromVersion, int toVersion, LocaleOptions? options = null, CancellationToken cancellationToken = default) =>
        DiffAsync(type, id, fromVersion, toVersion, options?.Locale, cancellationToken);

    internal async Task<IReadOnlyList<VersionChange>> DiffAsync(string type, Guid id, int fromVersion, int toVersion, string? locale, CancellationToken cancellationToken)
    {
        using var span = Span("diff", type);

        ArgumentNullException.ThrowIfNull(type);

        var (write, document) = await BeginVersionReadAsync(type, id, locale, cancellationToken);
        var before = await store.VersionAsync(id, document.Locale, fromVersion, cancellationToken);
        var after = await store.VersionAsync(id, document.Locale, toVersion, cancellationToken);
        if (before is null || after is null)
        {
            throw new EntryNotFoundException(write.Type.Name, id);
        }

        var from = VisibleFieldsOf(write, before);
        var to = VisibleFieldsOf(write, after);
        await DropUnreadableTargetsAsync(write, cancellationToken, from, to);
        return VersionDiff.Between(from, to);
    }

    // A preview key shows the draft as it is, not everything it has been, so versions also need the Management scope. An
    // unknown type and an unreadable one give the engine's one error, so that type names cannot be tried. Access goes by the
    // current row (spec 4.5): history is part of the entry.
    private async Task<(WriteScope Write, Document Document)> BeginVersionReadAsync(string type, Guid id, string? locale, CancellationToken cancellationToken)
    {
        var operation = Current();
        var resolved = await ResolveLocaleAsync(operation.Site, locale ?? operation.Locale, cancellationToken);
        if (schema.FindContentType(type) is not { } known || !(await evaluator.ResolveAsync(operation, cancellationToken)).Allows(known, ContentOperation.Read, new AccessTarget(operation.Site, resolved)))
        {
            throw new ValidationException([new ValidationError("type", QueryErrorCodes.UnknownField, $"'{type}' is not a content type that can be read.")]);
        }

        var write = await BeginAsync(type, ContentOperation.Read, cancellationToken, locale);
        if (!write.Permissions.CanManage)
        {
            throw new ContentAccessDeniedException(write.Type.Name, CorePermissions.OperationName(ContentOperation.Read));
        }

        var (_, document, _) = await ReachLocaleAsync(write, ContentOperation.Read, id, cancellationToken);
        return (write, document);
    }

    private async Task<(List<Document> Documents, Document Document, AccessTarget Target)> ReachLocaleAsync(WriteScope write, ContentOperation operation, Guid id, CancellationToken cancellationToken)
    {
        var documents = await store.DocumentsAsync(write.Type, id, write.Site, trashed: false, cancellationToken);
        var document = documents.Find(item => item.Locale == write.DocumentLocale)
            ?? throw new EntryNotFoundException(write.Type.Name, id);

        var target = write.Target with { EntryLocales = [.. documents.Select(item => item.Locale)], Found = true };
        if (!await evaluator.CanAccessEntryAsync(write.Operation, write.Type, operation, id, target, QueryStatus.Draft, cancellationToken))
        {
            throw new EntryNotFoundException(write.Type.Name, id);
        }

        return (documents, document, target);
    }

    private async Task ThrowIfNotExpectedAsync(WriteScope write, Guid id, Document document, int? expected, CancellationToken cancellationToken)
    {
        if (expected is { } version && version != document.DraftVersion)
        {
            throw await ConflictAsync(write, id, version, document.DraftVersion, cancellationToken);
        }
    }

    private static void ThrowIfNotPublished(WriteScope write, Document document)
    {
        if (document.PublishedVersion is null)
        {
            throw new ValidationException([new ValidationError("", ContentErrorCodes.NotPublished, $"This {write.Type.Name} is not published in this locale.")]);
        }
    }

    // A discard or restore answers with a status and a version that depend on whether the draft equals what it is compared
    // with. Where the principal can write a field it cannot read, that would check a saved guess, so it is refused whatever the field holds.
    private static void ThrowIfWritesBlind(WriteScope write, WritableFields writable)
    {
        if (!write.Permissions.IsSystem && write.Type.Fields.Any(field => !field.Hidden && !field.ReadOnly && writable.Includes(field.Name) && !write.Permissions.CanReadField(write.Type, field.Name, write.Target)))
        {
            throw new ContentAccessDeniedException(write.Type.Name, CorePermissions.OperationName(ContentOperation.Update));
        }
    }

    // Publishing moves a shared field for every locale published with it, so those are the ones a grant must cover.
    private async Task<WritableFields> CheckPublishGrantAsync(WriteScope write, List<Document> documents, Document document, AccessTarget target, ContentDocument draft, ContentDocument published, CancellationToken cancellationToken)
    {
        var moved = target with { EntryLocales = [.. documents.Where(other => other.PublishedVersion is not null || other.Id == document.Id).Select(other => other.Locale)] };
        var writable = await evaluator.WritableFieldsAsync(write.Operation, write.Type, ContentOperation.Publish, document.DocumentId, moved, cancellationToken);
        await ThrowIfBeyondThePublishGrantAsync(write, document.DocumentId, target, draft, published, writable, cancellationToken);
        return writable;
    }

    // The whole entry goes out of sight, so a grant limited to some fields does not reach far enough.
    private async Task CheckUnpublishGrantAsync(WriteScope write, Document document, AccessTarget target, CancellationToken cancellationToken)
    {
        var writable = await evaluator.WritableFieldsAsync(write.Operation, write.Type, ContentOperation.Unpublish, document.DocumentId, target with { EntryLocales = [document.Locale] }, cancellationToken);
        if (write.Type.Fields.Any(field => !field.Hidden && !field.ReadOnly && !writable.Includes(field.Name)))
        {
            throw new ContentAccessDeniedException(write.Type.Name, CorePermissions.OperationName(ContentOperation.Unpublish));
        }
    }

    // A grant limited to some fields publishes only what stays within them. A field it can write but not read is refused
    // whatever it holds: otherwise it could save a guess and let this answer say whether it equals the published value.
    private async Task ThrowIfBeyondThePublishGrantAsync(WriteScope write, Guid id, AccessTarget target, ContentDocument draft, ContentDocument published, WritableFields writable, CancellationToken cancellationToken)
    {
        if (writable.Allowed && writable.Fields is null)
        {
            return;
        }

        var updatable = await evaluator.WritableFieldsAsync(write.Operation, write.Type, ContentOperation.Update, id, target, cancellationToken);
        foreach (var field in write.Type.Fields.Where(field => !field.Hidden && !field.ReadOnly && !writable.Includes(field.Name)))
        {
            var from = published.GetValueOrDefault(field.Name);
            var to = draft.GetValueOrDefault(field.Name);
            var isSame = EntryJson.Same(from, to) || (LocaleFallbackReader.IsMissing(from) && LocaleFallbackReader.IsMissing(to));
            if (!isSame || (updatable.Includes(field.Name) && !write.Permissions.CanReadField(write.Type, field.Name, write.Target)))
            {
                throw new ContentAccessDeniedException(write.Type.Name, CorePermissions.OperationName(ContentOperation.Publish));
            }
        }
    }

    // A publish makes no slug: the draft's values are what was reviewed.
    private (ContentDocument Working, JsonObject Fields) Settle(WriteScope write, ContentDocument working, JsonObject baseline, ContentDocument stored, WritableFields writable)
    {
        var canonical = canonicalizer.Canonicalize(write.Type, working, static (_, _) => false, stored);
        var fields = EntryJson.Fields(write.Type, canonical);
        write.Access.RejectHookChanges(write.Type.Fields.Select(field => field.Name).Where(name => !JsonNode.DeepEquals(baseline[name], fields[name])), write.Type, writable);
        return (canonical, fields);
    }

    // fields is EntryJson.Fields of working.
    private async Task ValidateForPublishAsync(WriteScope write, ContentDocument working, JsonObject fields, Document document, AccessTarget target, List<PublishWarning> warnings, CancellationToken cancellationToken)
    {
        warnings.Clear();
        var entry = write.Type.ClrType is { } clrType ? mapper.ToEntry(clrType, working) : null;
        var result = await validation.ValidateAsync(
            write.Type,
            mapper.ValidationValues(write.Type, working),
            entry,
            write.Operation,
            ValidationMode.Publish,
            cancellationToken,
            async (context, token) =>
            {
                await store.CheckUniqueAsync(write.Type, working, document.DocumentId, write.Site, write.Locale, context, token, publishing: true);
                await CheckReferencesAsync(write, working, document.DocumentId, target, context, warnings, token, tolerateTargetState: importMode.Active);
            });

        var errors = new EntryErrors(schema, write.Type, write.Permissions, write.Target).ForPublish(result.Errors, fields);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    // A write takes any id without looking, so a warning or a refusal that told a missing target from a published one would
    // be a way to ask about any entry. What a principal could have chosen is therefore never confirmed to it (spec 4.5).
    // A self-reference is exempt: it is what is being published.
    private async Task CheckReferencesAsync(WriteScope write, ContentDocument working, Guid id, AccessTarget entryTarget, ValidationContext context, List<PublishWarning> warnings, CancellationToken cancellationToken, bool tolerateTargetState = false)
    {
        var blocks = write.Type.UnpublishedReferences == UnpublishedReferencePolicy.Block;
        var locales = (await fallback.ChainAsync(write.Site, write.Locale, LocaleFallback.Chain, cancellationToken)).Locales;
        var isSystem = write.Permissions.IsSystem;
        HashSet<(string Path, string Code, Guid? Id)> told = [];
        WritableFields? updatable = null;
        WritableFields? creatable = null;

        void Refuse() => context.AddError("", PublishWarningCodes.UnavailableReference, "The entry refers to an entry the principal cannot confirm.");

        bool IsChosenBlind(EntryTarget reference) => !isSystem && !(reference.IsShown && write.Permissions.CanReadField(write.Type, reference.Field, write.Target));

        // What is asked of the targets is asked of all of them first, a query or two per target type, and then answered in order.
        var references = store.TargetsOf(write.Type, working).Where(reference => reference.Id != id || reference.TargetType != write.Type.Name).ToList();
        var published = tolerateTargetState || !blocks ? [] : await PublishedTargetsAsync(write, references.Where(IsChosenBlind), locales, cancellationToken);
        var states = tolerateTargetState
            ? []
            : await TargetStatesAsync(write, references.Where(reference => !IsChosenBlind(reference) && IsReadableType(write, reference.TargetType)), locales, cancellationToken);

        foreach (var reference in references)
        {
            if (IsChosenBlind(reference))
            {
                if (blocks)
                {
                    updatable ??= await evaluator.WritableFieldsAsync(write.Operation, write.Type, ContentOperation.Update, id, entryTarget, cancellationToken);
                    creatable ??= await evaluator.WritableFieldsAsync(write.Operation, write.Type, ContentOperation.Create, id, entryTarget, cancellationToken);
                    var chose = reference.IsShown && (updatable.Includes(reference.Field) || creatable.Includes(reference.Field));
                    if (chose || (!tolerateTargetState && !published.Contains((reference.TargetType, reference.Id))))
                    {
                        Refuse();
                    }
                }

                continue;
            }

            if (!IsReadableType(write, reference.TargetType))
            {
                if (blocks)
                {
                    Refuse();
                }

                continue;
            }

            if (tolerateTargetState)
            {
                continue;
            }

            var state = states[(reference.TargetType, reference.Id)];
            if (state == ReferenceTargetState.Published)
            {
                continue;
            }

            var unpublished = state == ReferenceTargetState.Unpublished;
            var code = unpublished ? PublishWarningCodes.UnpublishedReference : PublishWarningCodes.UnavailableReference;
            var message = unpublished ? $"'{reference.Path}' refers to an entry that is not published." : $"'{reference.Path}' refers to an entry that cannot be reached.";
            if (blocks)
            {
                context.AddError(reference.Field, code, message);
            }
            else
            {
                var named = unpublished || isSystem ? reference.Id : (Guid?)null;
                if (told.Add((reference.Path, code, named)))
                {
                    warnings.Add(new PublishWarning(reference.Path, code, message, named is null ? null : reference.TargetType, named));
                }
            }
        }
    }

    private bool IsReadableType(WriteScope write, string typeName) =>
        write.Permissions.IsSystem || (schema.FindContentType(typeName) is { } target && write.Permissions.Allows(target, ContentOperation.Read, write.Target));

    // The targets that are published, asked whatever the principal may read of them, in one query per target type.
    private async Task<HashSet<(string Type, Guid Id)>> PublishedTargetsAsync(WriteScope write, IEnumerable<EntryTarget> references, IReadOnlyList<string> locales, CancellationToken cancellationToken)
    {
        HashSet<(string Type, Guid Id)> published = [];
        foreach (var (target, ids) in ByTargetType(references))
        {
            foreach (var (id, rows) in await store.TargetRowsAsync(target, ids, write.Site, cancellationToken))
            {
                if (IsPublishedIn(target, rows, locales))
                {
                    published.Add((target.Name, id));
                }
            }
        }

        return published;
    }

    private static bool IsPublishedIn(SchemaType target, List<ReferenceTargetRow> rows, IReadOnlyList<string> locales) =>
        rows.Any(row => row.IsPublished && (!target.Localized || locales.Contains(row.Locale, StringComparer.OrdinalIgnoreCase)));

    // What a principal that may read the type may know of each target. A row only in locales its grant does not cover, and a
    // row its rule refuses, read the same as no row. The rule is asked of the locale that serves the target, not the entry's,
    // so the targets of one type are asked in one query for their rows and one per locale that serves them.
    private async Task<Dictionary<(string Type, Guid Id), ReferenceTargetState>> TargetStatesAsync(WriteScope write, IEnumerable<EntryTarget> references, IReadOnlyList<string> locales, CancellationToken cancellationToken)
    {
        List<EntryTarget> all = [.. references];
        Dictionary<(string Type, Guid Id), ReferenceTargetState> states = [];
        foreach (var reference in all)
        {
            states[(reference.TargetType, reference.Id)] = ReferenceTargetState.Unavailable;
        }

        var isSystem = write.Permissions.IsSystem;
        foreach (var (target, ids) in ByTargetType(all))
        {
            var found = await store.TargetRowsAsync(target, ids, write.Site, cancellationToken);
            Dictionary<string, List<Guid>> toAsk = new(StringComparer.Ordinal);
            foreach (var id in ids)
            {
                var rows = found.GetValueOrDefault(id) ?? [];
                if (!isSystem)
                {
                    rows = [.. rows.Where(row => !target.Localized || write.Permissions.Allows(target, ContentOperation.Read, write.Target with { Locale = row.Locale }))];
                }

                if (rows.Count == 0)
                {
                    continue;
                }

                states[(target.Name, id)] = IsPublishedIn(target, rows, locales) ? ReferenceTargetState.Published : ReferenceTargetState.Unpublished;
                if (!isSystem)
                {
                    var serving = locales.Select(locale => rows.Find(row => row.IsPublished && string.Equals(row.Locale, locale, StringComparison.OrdinalIgnoreCase))).FirstOrDefault(row => row is not null)
                        ?? locales.Select(locale => rows.Find(row => string.Equals(row.Locale, locale, StringComparison.OrdinalIgnoreCase))).FirstOrDefault(row => row is not null)
                        ?? rows[0];
                    var locale = target.Localized ? serving.Locale : "";
                    if (!toAsk.TryGetValue(locale, out var waiting))
                    {
                        waiting = [];
                        toAsk[locale] = waiting;
                    }

                    waiting.Add(id);
                }
            }

            foreach (var (locale, waiting) in toAsk)
            {
                var asked = target.Localized ? write.Target with { Locale = locale } : write.Target;
                var accessible = await evaluator.AccessibleEntriesAsync(write.Operation, target, ContentOperation.Read, waiting, asked, QueryStatus.Draft, cancellationToken);
                foreach (var id in waiting.Where(id => !accessible.Contains(id)))
                {
                    states[(target.Name, id)] = ReferenceTargetState.Unavailable;
                }
            }
        }

        return states;
    }

    // An unknown type has no rows to ask about.
    private IEnumerable<(SchemaType Target, List<Guid> Ids)> ByTargetType(IEnumerable<EntryTarget> references) =>
        references
            .GroupBy(reference => reference.TargetType, StringComparer.Ordinal)
            .Select(group => (Target: schema.FindContentType(group.Key), Ids: group.Select(reference => reference.Id).Distinct().ToList()))
            .Where(group => group.Target is not null)
            .Select(group => (group.Target!, group.Ids));

    // rows are the draft rows this transaction read, stored the draft they hold, and fields EntryJson.Fields of working.
    // Answers the other locales whose versions moved with the shared fields. A publish sets its own status after it, and a
    // discard, which held its row before it read what decides its status, has it from that read.
    private async Task<List<Document>> WriteOverDraftAsync(WriteScope write, Guid id, List<Document> documents, Document document, StoredRows rows, ContentDocument stored, ContentDocument working, JsonObject fields, EntryStatus status, string? message, CancellationToken cancellationToken, HeldRows? held = null, int? movedBefore = null)
    {
        var moved = SharedChanged(write.Type, fields, stored) ? documents : [];
        var read = document.Status;
        var advanced = await AdvanceVersionsAsync(write, id, document, document.DraftVersion, status, moved, cancellationToken, held, held is null ? StatusOf(write, fields, cancellationToken) : null, movedBefore);
        if (held is null)
        {
            await SettleOwnStatusAsync(write, document, read, fields, cancellationToken);
        }

        await store.WriteDraftAsync(write.Type, document, isNewDocument: false, rows, working, fields, write.Operation, cancellationToken, message);
        return advanced;
    }

    // The status of a write's own locale, from its row as stored and, when published, its published rows read now.
    // fields is EntryJson.Fields of what the write stores.
    private Func<Document, Task<EntryStatus>> StatusOf(WriteScope write, JsonObject fields, CancellationToken cancellationToken) =>
        async row => row.PublishedVersion is null
            ? row.Status
            : SameFields(write.Type, fields, await store.ReadAsync(write.Type, row, ContentState.Published, cancellationToken)) ? EntryStatus.Published : EntryStatus.Modified;

    // A write decides its status from the row it read, and what decides it can move before the swap that holds the row: a
    // publish or an unpublish of this locale, which the swap finds by comparing the published version and the status, or
    // another locale's publish, which copies the shared row and holds this row while it does. So the status is decided
    // again once the swap holds the row, from what is stored (#3524). A locale read Published at the published version and
    // status it still has is as it was read: a shared row copied since is the one its draft holds, or the swap would have
    // lost. A Modified one reads its published rows now, which the write would have read before its swap.
    // fields is EntryJson.Fields of what the write stores; read is the status the write read.
    private async Task SettleOwnStatusAsync(WriteScope write, Document document, EntryStatus read, JsonObject fields, CancellationToken cancellationToken)
    {
        if (document.PublishedVersion is null || read == EntryStatus.Published)
        {
            return;
        }

        var status = await StatusOf(write, fields, cancellationToken)(document);
        if (status != document.Status)
        {
            await store.SetStatusAsync(document, status, cancellationToken);
            document.Status = status;
        }
    }

    // Asked after the entry's draft was written over, so it is known to be in its site.
    private async Task ThrowIfOutOfReachAsync(WriteScope write, ContentOperation operation, Guid id, CancellationToken cancellationToken)
    {
        if (!await evaluator.CanAccessEntryAsync(write.Operation, write.Type, operation, id, write.Target with { Found = true }, QueryStatus.Draft, cancellationToken))
        {
            throw new ContentAccessDeniedException(write.Type.Name, CorePermissions.OperationName(operation));
        }
    }

    // The shared row moved under the other locales, and each is Published only while its draft equals what is published.
    // The rows are read again here, after the swaps that lock the advanced ones, so a publish or an unpublish that committed
    // since this write read them decides with them (#3265). Each advanced locale gets a version of its draft as it now is.
    // Every row it sets is held: a write that moved the shared row holds every other locale, and so does a publish.
    private async Task RecomputeOtherLocalesAsync(WriteScope write, Document document, CancellationToken cancellationToken, IReadOnlyCollection<Document>? advanced = null, string? message = null)
    {
        if (!write.Type.Localized)
        {
            return;
        }

        var moved = (advanced ?? []).Select(row => row.Locale).ToHashSet(StringComparer.Ordinal);
        var others = await store.DocumentsAsync(write.Type, document.DocumentId, write.Site, trashed: false, cancellationToken);
        foreach (var other in others.Where(other => other.Locale != document.Locale && (moved.Contains(other.Locale) || other.PublishedVersion is not null)))
        {
            var draft = await store.ReadAsync(write.Type, other, ContentState.Draft, cancellationToken);
            var status = other.Status;
            if (other.PublishedVersion is not null)
            {
                var published = await store.ReadAsync(write.Type, other, ContentState.Published, cancellationToken);
                status = SameFields(write.Type, draft, published) ? EntryStatus.Published : EntryStatus.Modified;
            }

            if (status != other.Status)
            {
                await store.SetStatusAsync(other, status, cancellationToken);
                other.Status = status;
            }

            if (moved.Contains(other.Locale))
            {
                if (other.SchedulePublishAt is not null)
                {
                    await schedules.MovePendingPublishAsync(other.DocumentId, other.Locale, other.DraftVersion - 1, other.DraftVersion, cancellationToken);
                }

                await store.AddVersionAsync(write.Type, other, EntryJson.Fields(write.Type, draft), write.Operation, message ?? SharedMessage(document), cancellationToken);
            }
        }
    }

    private static string SharedMessage(Document document) => string.Create(CultureInfo.InvariantCulture, $"Shared fields changed in {document.Locale}.");

    // The one lock order of the writes of an entry (spec 4.5, "Lock order as built"; #3524). A path takes the entry's
    // cms_document rows that it will write first, one statement each, by primary key and ordinal by locale code, and only
    // then its content rows of either state, its references and versions, and last its schedules and their jobs; a row of
    // an earlier kind is written afterwards only once it is held, as a status or the mirror of a schedule is. So two writes,
    // publishes or jobs of one entry that cross wait for each other instead of deadlocking. A path takes its rows here, or
    // by the swaps of AdvanceVersionsAsync, which follow the same order.
    private async Task HoldInOrderAsync(IEnumerable<Document> rows, CancellationToken cancellationToken)
    {
        foreach (var row in rows.OrderBy(row => row.Locale, StringComparer.Ordinal))
        {
            await store.HoldAsync(row, cancellationToken);
        }
    }

    // Holds rows read before the transaction held any, and reads the entry's rows again: one added between, which the holds
    // do not cover, would be taken out of the lock order or written unchecked (#3524). The write then runs again in a
    // transaction of its own, which reads it; elsewhere it answers conflict, which a client retries.
    private async Task HoldEveryRowAsync(Guid id, IReadOnlyCollection<Document> rows, Func<Task<Exception>>? conflict, CancellationToken cancellationToken)
    {
        await HoldInOrderAsync(rows, cancellationToken);
        if ((await store.AllRowsAsync(id, cancellationToken)).Any(row => !rows.Any(held => held.Id == row.Id)))
        {
            throw new EntryRowsAddedException(conflict is null ? null : await conflict());
        }
    }

    // Every compare and swap of one write, in the lock order, so that two writes that cross wait for each other rather than
    // deadlock (#3265): the write's own locale, unless it adds that locale, and each other locale whose draft moved with the
    // shared fields, from the version this transaction read, and those a publish or a discard holds. The own row is locked in
    // its place in that order but moved last, so that a conflict at any swap reads it as stored, never as this write would
    // have left it, and only held where status is null. Another locale's swap moves its version, updatedAt and updatedBy
    // alone: its status is the recompute's. With decide, the own swap compares the published version and the status the
    // write read too, and when only those moved the row is held as it is and the status decided again (#3524). With
    // movedBefore, the mark taken before the write's hooks, a swap lost to one of them is the hook's (#3568). Answers the
    // other locales advanced.
    private async Task<List<Document>> AdvanceVersionsAsync(WriteScope write, Guid id, Document? own, int expected, EntryStatus? status, IEnumerable<Document> moved, CancellationToken cancellationToken, HeldRows? holds = null, Func<Document, Task<EntryStatus>>? decide = null, int? movedBefore = null)
    {
        async Task<Exception> LostAsync(string locale) =>
            movedBefore is { } mark && MovedByHook(id, locale, mark) is { } refused ? refused
            : holds is null ? await SwapConflictAsync(write, id, expected, cancellationToken)
            : await holds.Lost(locale);

        var at = write.Operation.Timestamp;
        var by = write.Operation.AuditPrincipal.Id;
        var others = moved.Where(other => own is null || other.Locale != own.Locale).OrderBy(other => other.Locale, StringComparer.Ordinal).ToList();
        var rows = others
            .Concat((holds?.Held ?? []).Where(row => (own is null || row.Locale != own.Locale) && !others.Any(other => other.Locale == row.Locale)))
            .OrderBy(row => row.Locale, StringComparer.Ordinal)
            .ToList();
        var held = false;
        foreach (var row in rows)
        {
            if (!held && own is not null && string.CompareOrdinal(own.Locale, row.Locale) < 0)
            {
                held = await store.HoldVersionAsync(own, expected, cancellationToken);
                if (!held)
                {
                    throw await LostAsync(own.Locale);
                }
            }

            if (!others.Contains(row))
            {
                await store.HoldAsync(row, cancellationToken);
                continue;
            }

            if (!await store.AdvanceDraftVersionAsync(row, row.DraftVersion, at, by, cancellationToken))
            {
                throw await LostAsync(row.Locale);
            }

            _moved.Add((id, row.Locale));
            row.DraftVersion++;
            row.UpdatedAt = at;
            row.UpdatedBy = by;
        }

        if (own is not null && status is { } moving)
        {
            var read = decide is null ? null : new StoredState(own.PublishedVersion, own.Status);
            if (!await store.AdvanceVersionAsync(own, expected, moving, at, by, cancellationToken, read))
            {
                // A publish or an unpublish of this locale since the read moved its published version or status alone
                // (#3524): the row is held as it is, and the status decided again from it.
                if (read is null || !await store.HoldVersionAsync(own, expected, cancellationToken))
                {
                    throw await LostAsync(own.Locale);
                }

                (own.PublishedVersion, own.Status) = await store.StateAsync(own, cancellationToken);
                moving = await decide!(own);
                if (!await store.AdvanceVersionAsync(own, expected, moving, at, by, cancellationToken))
                {
                    throw await LostAsync(own.Locale);
                }
            }

            _moved.Add((id, own.Locale));
            own.DraftVersion = expected + 1;
            own.Status = moving;
            own.UpdatedAt = at;
            own.UpdatedBy = by;
        }
        else if (own is not null && !held && !await store.HoldVersionAsync(own, expected, cancellationToken))
        {
            throw await LostAsync(own.Locale);
        }

        return others;
    }

    // A write's own row lost its compare: 412 with the version stored, or not found for a row in the trash or gone.
    private async Task<Exception> OwnRowLostAsync(WriteScope write, Guid id, int expected, CancellationToken cancellationToken) =>
        await store.LiveDraftVersionAsync(id, write.DocumentLocale, cancellationToken) is { } stored
            ? await ConflictAsync(write, id, expected, stored, cancellationToken)
            : new EntryNotFoundException(write.Type.Name, id);

    // Whichever swap lost, the answer is of the locale written, read now that the winner has committed: the version the
    // winner gave it or added it at, or the client's own when only another locale moved.
    private async Task<VersionConflictException> SwapConflictAsync(WriteScope write, Guid id, int expected, CancellationToken cancellationToken) =>
        await ConflictAsync(write, id, expected, await store.DraftVersionAsync(id, write.DocumentLocale, cancellationToken), cancellationToken);

    private static bool SharedChanged(SchemaType type, JsonObject fields, ContentDocument stored) =>
        type.Localized && type.Fields.Any(field => !field.Localizable && !JsonNode.DeepEquals(fields[field.Name], EntryJson.ToNode(stored.GetValueOrDefault(field.Name))));

    private static bool SameFields(SchemaType type, ContentDocument left, ContentDocument right) =>
        JsonNode.DeepEquals(EntryJson.Fields(type, left), EntryJson.Fields(type, right));

    // fields is EntryJson.Fields of the one side; the other is compared field by field rather than copied whole.
    private static bool SameFields(SchemaType type, JsonObject fields, ContentDocument entry) =>
        type.Fields.All(field => JsonNode.DeepEquals(fields[field.Name], EntryJson.ToNode(entry.GetValueOrDefault(field.Name))));

    private async Task<JsonObject> ReadableFieldsOfAsync(WriteScope write, ContentVersion version, CancellationToken cancellationToken)
    {
        var fields = VisibleFieldsOf(write, version);
        await DropUnreadableTargetsAsync(write, cancellationToken, fields);
        return fields;
    }

    // Hidden fields show to no one, the system principal included.
    private JsonObject VisibleFieldsOf(WriteScope write, ContentVersion version)
    {
        var snapshot = VersionSnapshot.Parse(schema, write.Type, version.Snapshot, richTextOptions.Links);
        JsonObject fields = [];
        foreach (var field in write.Type.Fields)
        {
            if (!field.Hidden && write.Permissions.CanReadField(write.Type, field.Name, write.Target) && snapshot[field.Name] is { } value)
            {
                var visible = value.DeepClone();
                HiddenFields.Remove(schema, visible, field, dropUnknown: true);
                fields[field.Name] = visible;
            }
        }

        return fields;
    }

    // Anyone but the system principal takes only what it could send itself, so that an old version is no way past the limits of a write.
    private async Task<ContentDocument> RestorableFieldsAsync(WriteScope write, ContentVersion version, WritableFields writable, CancellationToken cancellationToken)
    {
        var isSystem = write.Permissions.IsSystem;
        var snapshot = VersionSnapshot.Parse(schema, write.Type, version.Snapshot, richTextOptions.Links);
        var taken = write.Type.Fields.Where(field => isSystem || !Keeps(write, field, writable, addsLocale: false)).ToList();
        JsonObject fields = [];
        foreach (var field in taken)
        {
            if (snapshot[field.Name] is { } value)
            {
                var kept = value.DeepClone();
                HiddenFields.RemoveWhere(schema, kept, field, inner => !isSystem && (inner.Hidden || inner.ReadOnly));
                fields[field.Name] = kept;
            }
        }

        await DropUnreadableTargetsAsync(write, cancellationToken, fields);
        ContentDocument restored = [];
        foreach (var field in taken)
        {
            restored[field.Name] = fields[field.Name]?.DeepClone();
        }

        return restored;
    }

    // Fails closed on a target since trashed, which only a draft read of the entry itself would show. The targets of all the
    // snapshots are asked together, one query or two per target type.
    private async Task DropUnreadableTargetsAsync(WriteScope write, CancellationToken cancellationToken, params JsonObject[] snapshots)
    {
        if (write.Permissions.IsSystem)
        {
            return;
        }

        var nested = new NestedTargets(schema);
        Dictionary<string, HashSet<Guid>> wanted = new(StringComparer.Ordinal);
        foreach (var fields in snapshots)
        {
            foreach (var (name, ids) in nested.Collect(write.Type, fields))
            {
                if (wanted.TryGetValue(name, out var known))
                {
                    known.UnionWith(ids);
                }
                else
                {
                    wanted[name] = ids;
                }
            }
        }

        HashSet<(string Type, Guid Id)> readable = [];
        foreach (var (name, ids) in wanted)
        {
            if (schema.FindContentType(name) is not { } target || !write.Permissions.Allows(target, ContentOperation.Read, write.Target))
            {
                continue;
            }

            foreach (var id in await evaluator.AccessibleEntriesAsync(write.Operation, target, ContentOperation.Read, ids, write.Target, QueryStatus.Draft, cancellationToken))
            {
                readable.Add((name, id));
            }
        }

        foreach (var fields in snapshots)
        {
            nested.Filter(write.Type, fields, (name, id) => readable.Contains((name, id)));
        }
    }

    private object? AsRead(SchemaField field, object? value)
    {
        var name = EntryPaths.ReferenceTarget(field);
        return value switch
        {
            Guid id when field.Relation is not null => Stub(id, name),
            Guid[] ids when field.Relation is not null => ids.Select(id => Stub(id, name)).ToList(),
            ContentDocument group when schema.FindComponent((string)field.Settings[FieldSettings.Component]) is { } component => GroupAsRead(component, group),
            _ => value,
        };
    }

    private ContentDocument GroupAsRead(SchemaType component, ContentDocument group)
    {
        ContentDocument result = [];
        foreach (var (key, value) in group)
        {
            result[key] = component.FindField(key) is { } inner ? AsRead(inner, value) : value;
        }

        return result;
    }

    private static ContentDocument Stub(Guid id, string type) => new() { ["id"] = id, ["type"] = type };
}
