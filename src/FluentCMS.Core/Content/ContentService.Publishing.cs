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
using FluentCMS.Publishing;
using FluentCMS.Schema;

namespace FluentCMS.Core.Content;

// The transitions of spec 4.5 and the versions behind them. Each is a write like any other: the permission, the entry and
// its row rule, then in one transaction the field limits, the hooks, validation and the write, and the sink and the
// response only after the commit. What a version holds is read and restored as this principal may, not as it was saved.
internal sealed partial class ContentService
{
    public async Task<PublishResult<ContentDocument>> PublishAsync(string type, Guid id, string? locale = null, int? expectedVersion = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);

        var write = await BeginAsync(type, ContentOperation.Publish, cancellationToken, locale);
        List<PublishWarning> warnings = [];
        var row = await InTransactionAsync(
            async () =>
            {
                var (documents, document, target) = await ReachLocaleAsync(write, ContentOperation.Publish, id, cancellationToken);
                await ThrowIfNotExpectedAsync(write, id, document, expectedVersion, cancellationToken);

                var draft = await store.ReadAsync(write.Type, document, ContentState.Draft, cancellationToken);
                var published = await store.ReadAsync(write.Type, document, ContentState.Published, cancellationToken);
                var previous = document.PublishedVersion is null ? null : published;

                // Publishing moves a shared field for every locale published with it, so those are the ones a grant must cover.
                var moved = target with { EntryLocales = [.. documents.Where(other => other.PublishedVersion is not null || other.Id == document.Id).Select(other => other.Locale)] };
                var writable = await evaluator.WritableFieldsAsync(write.Operation, write.Type, ContentOperation.Publish, id, moved, cancellationToken);
                ThrowIfMovesWhatItMayNotWrite(write, draft, published, writable, ContentOperation.Publish);

                var original = EntryJson.Fields(write.Type, draft);
                var working = ContentCopy.Of(draft);
                await hooks.RunAsync(HookKind.BeforeValidate, write.Type, write.Operation, working, CopyOf(previous), cancellationToken);
                working = Settle(write, working, original, draft, writable);
                await ValidateForPublishAsync(write, working, document, warnings, cancellationToken);

                var validated = EntryJson.Fields(write.Type, working);
                await hooks.RunAsync(HookKind.BeforePublish, write.Type, write.Operation, working, CopyOf(previous), cancellationToken);
                working = Settle(write, working, validated, draft, writable);
                var settled = EntryJson.Fields(write.Type, working);
                if (!JsonNode.DeepEquals(validated, settled))
                {
                    await ValidateForPublishAsync(write, working, document, warnings, cancellationToken);
                }

                // What a hook changed is a new draft version, so that the published state is a version like any other.
                if (!JsonNode.DeepEquals(original, settled))
                {
                    var status = document.Status == EntryStatus.Published ? EntryStatus.Modified : document.Status;
                    await WriteOverDraftAsync(write, id, document, working, status, null, cancellationToken);
                    await ThrowIfOutOfReachAsync(write, ContentOperation.Publish, id, cancellationToken);
                }

                await store.CopyDraftToPublishedAsync(write.Type, document, cancellationToken);
                if (!await store.MarkPublishedAsync(document, write.Operation.Timestamp, write.Operation.AuditPrincipal.Id, cancellationToken))
                {
                    throw await ConflictAsync(write, id, document.DraftVersion, await store.DraftVersionAsync(id, document.Locale, cancellationToken), cancellationToken);
                }

                await RecomputeOtherLocalesAsync(write, documents, document, cancellationToken);

                EntryStore.ApplySystem(working, document);
                await hooks.RunAsync(HookKind.AfterPublish, write.Type, write.Operation, working, CopyOf(previous), cancellationToken);
                _pending.Add(new EntryChange(EntryChangeKind.Published, write.Type.Name, id, document.Locale, document.DraftVersion, write.Operation));
                return document;
            },
            cancellationToken);

        return new PublishResult<ContentDocument>(await RespondAsync(write, row), warnings);
    }

    public async Task<ContentDocument> UnpublishAsync(string type, Guid id, string? locale = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);

        var write = await BeginAsync(type, ContentOperation.Unpublish, cancellationToken, locale);
        var row = await InTransactionAsync(
            async () =>
            {
                var (documents, document, target) = await ReachLocaleAsync(write, ContentOperation.Unpublish, id, cancellationToken);
                ThrowIfNotPublished(write, document);

                // The whole entry goes out of sight, so a grant limited to some fields does not reach far enough.
                var writable = await evaluator.WritableFieldsAsync(write.Operation, write.Type, ContentOperation.Unpublish, id, target with { EntryLocales = [document.Locale] }, cancellationToken);
                if (write.Type.Fields.Any(field => !field.Hidden && !field.ReadOnly && !writable.Includes(field.Name)))
                {
                    throw new ContentAccessDeniedException(write.Type.Name, CorePermissions.OperationName(ContentOperation.Unpublish));
                }

                var published = await store.ReadAsync(write.Type, document, ContentState.Published, cancellationToken);
                var working = ContentCopy.Of(published);
                await hooks.RunAsync(HookKind.BeforeUnpublish, write.Type, write.Operation, working, ContentCopy.Of(published), cancellationToken);

                var isLast = !documents.Any(other => other.Id != document.Id && other.PublishedVersion is not null);
                await store.DeletePublishedAsync(write.Type, document, isLast, cancellationToken);
                await store.MarkUnpublishedAsync(document, write.Operation.Timestamp, write.Operation.AuditPrincipal.Id, cancellationToken);

                await hooks.RunAsync(HookKind.AfterUnpublish, write.Type, write.Operation, working, ContentCopy.Of(published), cancellationToken);
                _pending.Add(new EntryChange(EntryChangeKind.Unpublished, write.Type.Name, id, document.Locale, document.DraftVersion, write.Operation));
                return document;
            },
            cancellationToken);

        return await RespondAsync(write, row);
    }

    public async Task<ContentDocument> DiscardDraftAsync(string type, Guid id, string? locale = null, int? expectedVersion = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);

        var write = await BeginAsync(type, ContentOperation.Update, cancellationToken, locale);
        var row = await InTransactionAsync(
            async () =>
            {
                var (documents, document, target) = await ReachLocaleAsync(write, ContentOperation.Update, id, cancellationToken);
                ThrowIfNotPublished(write, document);
                await ThrowIfNotExpectedAsync(write, id, document, expectedVersion, cancellationToken);

                var stored = await store.ReadAsync(write.Type, document, ContentState.Draft, cancellationToken);
                var published = await store.ReadAsync(write.Type, document, ContentState.Published, cancellationToken);
                if (SameFields(write.Type, stored, published))
                {
                    return document;
                }

                // The published values go back whole for a principal that may write all of them. For one that may not, it is
                // as an update: only what it may write changes, and the hidden and read-only values stay with what it left.
                var writable = await evaluator.WritableFieldsAsync(write.Operation, write.Type, ContentOperation.Update, id, target, cancellationToken);
                ContentDocument incoming = [];
                foreach (var field in write.Type.Fields.Where(field => writable.Allowed && (writable.Fields is null || (!field.Hidden && !field.ReadOnly && writable.Includes(field.Name)))))
                {
                    incoming[field.Name] = published.GetValueOrDefault(field.Name);
                }

                var working = Prepare(write, EntryComposer.Replace(write.Type, stored, incoming, static _ => true), document, stored);
                working = await RunBeforeHooksAsync(write, HookKind.BeforeUpdate, working, new SaveContext(stored, [], writable, document), cancellationToken);

                await WriteOverDraftAsync(write, id, document, working, SameFields(write.Type, working, published) ? EntryStatus.Published : EntryStatus.Modified, "Discarded the draft.", cancellationToken);
                await RecomputeOtherLocalesAsync(write, documents, document, cancellationToken);
                await ThrowIfOutOfReachAsync(write, ContentOperation.Update, id, cancellationToken);

                EntryStore.ApplySystem(working, document);
                await hooks.RunAsync(HookKind.AfterUpdate, write.Type, write.Operation, working, ContentCopy.Of(stored), cancellationToken);
                _pending.Add(new EntryChange(EntryChangeKind.DraftDiscarded, write.Type.Name, id, document.Locale, document.DraftVersion, write.Operation));
                return document;
            },
            cancellationToken);

        return await RespondAsync(write, row);
    }

    public async Task<IReadOnlyList<VersionInfo>> GetVersionsAsync(string type, Guid id, string? locale = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);

        var (_, document) = await BeginVersionReadAsync(type, id, locale, cancellationToken);
        var versions = await store.VersionsAsync(id, document.Locale, cancellationToken);
        return [.. versions.Select(version => new VersionInfo(version.Number, version.Locale, version.StatusAfter, version.AuthorId, version.Origin, version.ClientId, version.Message, version.CreatedAt))];
    }

    public async Task<ContentDocument?> GetVersionAsync(string type, Guid id, int number, string? locale = null, CancellationToken cancellationToken = default)
    {
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

    public async Task<ContentDocument> RestoreVersionAsync(string type, Guid id, int number, string? locale = null, int? expectedVersion = null, CancellationToken cancellationToken = default)
    {
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
                var stored = await store.ReadAsync(write.Type, document, ContentState.Draft, cancellationToken);
                var restored = await RestorableFieldsAsync(write, version, writable, cancellationToken);

                var isSystem = write.Permissions.IsSystem;
                var composed = EntryComposer.Replace(write.Type, stored, restored, field => !isSystem && Keeps(write, field, writable, addsLocale: false));
                var working = Prepare(write, composed, document, stored);
                if (!isSystem)
                {
                    composer.CarryProtected(write.Type, stored, working, includingGroups: true);
                    var shown = await ReadShownAsync(write, id, documents, addsLocale: false, cancellationToken);
                    await KeepUnreadableTargetsAsync(write, stored, shown, working, isPatch: false, cancellationToken);
                }

                working = await RunBeforeHooksAsync(write, HookKind.BeforeUpdate, working, new SaveContext(stored, Client(restored), writable, document), cancellationToken);

                var status = document.Status;
                if (document.PublishedVersion is not null)
                {
                    var published = await store.ReadAsync(write.Type, document, ContentState.Published, cancellationToken);
                    status = SameFields(write.Type, working, published) ? EntryStatus.Published : EntryStatus.Modified;
                }

                var message = string.Create(CultureInfo.InvariantCulture, $"Restored version {number}.");
                await WriteOverDraftAsync(write, id, document, working, status, message, cancellationToken);
                await RecomputeOtherLocalesAsync(write, documents, document, cancellationToken);
                await ThrowIfOutOfReachAsync(write, ContentOperation.Update, id, cancellationToken);

                EntryStore.ApplySystem(working, document);
                await hooks.RunAsync(HookKind.AfterUpdate, write.Type, write.Operation, working, ContentCopy.Of(stored), cancellationToken);
                _pending.Add(new EntryChange(EntryChangeKind.VersionRestored, write.Type.Name, id, document.Locale, document.DraftVersion, write.Operation));
                return document;
            },
            cancellationToken);

        return await RespondAsync(write, row);
    }

    public async Task<IReadOnlyList<VersionChange>> DiffAsync(string type, Guid id, int fromVersion, int toVersion, string? locale = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);

        var (write, document) = await BeginVersionReadAsync(type, id, locale, cancellationToken);
        var before = await store.VersionAsync(id, document.Locale, fromVersion, cancellationToken);
        var after = await store.VersionAsync(id, document.Locale, toVersion, cancellationToken);
        if (before is null || after is null)
        {
            throw new EntryNotFoundException(write.Type.Name, id);
        }

        return VersionDiff.Between(await ReadableFieldsOfAsync(write, before, cancellationToken), await ReadableFieldsOfAsync(write, after, cancellationToken));
    }

    // Versions are a draft's history, so they need what a draft read needs, and they are for those who manage content: a
    // preview key shows the draft as it is, not everything it has been.
    private async Task<(WriteScope Write, Document Document)> BeginVersionReadAsync(string type, Guid id, string? locale, CancellationToken cancellationToken)
    {
        var write = await BeginAsync(type, ContentOperation.Read, cancellationToken, locale);
        if (!write.Permissions.CanManage)
        {
            throw new ContentAccessDeniedException(write.Type.Name, CorePermissions.OperationName(ContentOperation.Read));
        }

        var (_, document, _) = await ReachLocaleAsync(write, ContentOperation.Read, id, cancellationToken);
        return (write, document);
    }

    // No row in the locale, in another site or of another type, or refused by the row rule all read as no entry.
    private async Task<(List<Document> Documents, Document Document, AccessTarget Target)> ReachLocaleAsync(WriteScope write, ContentOperation operation, Guid id, CancellationToken cancellationToken)
    {
        var documents = await store.DocumentsAsync(write.Type, id, write.Site, trashed: false, cancellationToken);
        var document = documents.Find(item => item.Locale == write.DocumentLocale)
            ?? throw new EntryNotFoundException(write.Type.Name, id);

        var target = write.Target with { EntryLocales = [.. documents.Select(item => item.Locale)] };
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

    // A grant limited to some fields publishes only what stays within them, and it does not say which it was not, since it
    // may not be able to read that field. A hidden or read-only field has no grant to ask; a hook is what fills it.
    private static void ThrowIfMovesWhatItMayNotWrite(WriteScope write, ContentDocument draft, ContentDocument published, WritableFields writable, ContentOperation operation)
    {
        foreach (var field in write.Type.Fields.Where(field => !field.Hidden && !field.ReadOnly && !writable.Includes(field.Name)))
        {
            var from = published.GetValueOrDefault(field.Name);
            var to = draft.GetValueOrDefault(field.Name);
            if (!(EntryJson.Same(from, to) || (LocaleFallbackReader.IsMissing(from) && LocaleFallbackReader.IsMissing(to))))
            {
                throw new ContentAccessDeniedException(write.Type.Name, CorePermissions.OperationName(operation));
            }
        }
    }

    // A publish makes no slug: the draft's values are what was reviewed. A hook may set one, and answers to the same limits as on a save.
    private ContentDocument Settle(WriteScope write, ContentDocument working, JsonObject baseline, ContentDocument stored, WritableFields writable)
    {
        var canonical = canonicalizer.Canonicalize(write.Type, working, static (_, _) => false, stored);
        var fields = EntryJson.Fields(write.Type, canonical);
        write.Access.RejectHookChanges(write.Type.Fields.Select(field => field.Name).Where(name => !JsonNode.DeepEquals(baseline[name], fields[name])), write.Type, writable);
        return canonical;
    }

    private async Task ValidateForPublishAsync(WriteScope write, ContentDocument working, Document document, List<PublishWarning> warnings, CancellationToken cancellationToken)
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
                await CheckReferencesAsync(write, working, context, warnings, token);
            });

        var errors = new EntryErrors(schema, write.Type, write.Permissions, write.Target).ForPublish(result.Errors, EntryJson.Fields(write.Type, working));
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    // Reference integrity, on what is about to be published. The answer must not be how a principal learns of what it may
    // not read: a write takes any id without looking, so a warning or a refusal that told a missing target from a published
    // one would be a way to ask about any entry. So a target of a type it may not read is never confirmed, and is no warning
    // and, where the type blocks, always a refusal; and one its row rule refuses reads the same as one that is not there.
    // A reference in a field it may not read is decided on what is true, and says nothing of where.
    private async Task CheckReferencesAsync(WriteScope write, ContentDocument working, ValidationContext context, List<PublishWarning> warnings, CancellationToken cancellationToken)
    {
        var blocks = write.Type.UnpublishedReferences == UnpublishedReferencePolicy.Block;
        var locales = (await fallback.ChainAsync(write.Site, write.Locale, LocaleFallback.Chain, cancellationToken)).Locales;
        var isSystem = write.Permissions.IsSystem;
        HashSet<(string Path, string Code, Guid? Id)> told = [];

        foreach (var reference in store.TargetsOf(write.Type, working))
        {
            var visible = isSystem || (reference.IsShown && write.Permissions.CanReadField(write.Type, reference.Field, write.Target));
            var target = schema.FindContentType(reference.TargetType);
            if (target is not null && !write.Permissions.Allows(target, ContentOperation.Read, write.Target))
            {
                if (blocks)
                {
                    context.AddError("", PublishWarningCodes.UnavailableReference, "The entry refers to an entry the principal cannot confirm.");
                }

                continue;
            }

            var state = target is null
                ? ReferenceTargetState.Unavailable
                : await store.TargetStateAsync(target, reference.Id, write.Site, locales, cancellationToken);

            if (visible && state != ReferenceTargetState.Unavailable
                && !await evaluator.CanAccessEntryAsync(write.Operation, target!, ContentOperation.Read, reference.Id, write.Target, QueryStatus.Draft, cancellationToken))
            {
                state = ReferenceTargetState.Unavailable;
            }

            if (state == ReferenceTargetState.Published)
            {
                continue;
            }

            if (!visible)
            {
                if (blocks)
                {
                    context.AddError("", PublishWarningCodes.UnavailableReference, "The entry refers to an entry the principal cannot confirm.");
                }

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

    // The save at the end of a transition: the version moves as on any save, under the same swap.
    private async Task WriteOverDraftAsync(WriteScope write, Guid id, Document document, ContentDocument working, EntryStatus status, string? message, CancellationToken cancellationToken)
    {
        var expected = document.DraftVersion;
        if (!await store.AdvanceVersionAsync(id, document.Locale, expected, status, write.Operation.Timestamp, write.Operation.AuditPrincipal.Id, cancellationToken))
        {
            throw await ConflictAsync(write, id, expected, await store.DraftVersionAsync(id, document.Locale, cancellationToken), cancellationToken);
        }

        document.DraftVersion = expected + 1;
        document.Status = status;
        document.UpdatedAt = write.Operation.Timestamp;
        document.UpdatedBy = write.Operation.AuditPrincipal.Id;
        await store.WriteDraftAsync(write.Type, document, isNewDocument: false, working, write.Operation.Origin, write.Operation.ClientId, cancellationToken, message);
    }

    private async Task ThrowIfOutOfReachAsync(WriteScope write, ContentOperation operation, Guid id, CancellationToken cancellationToken)
    {
        if (!await evaluator.CanAccessEntryAsync(write.Operation, write.Type, operation, id, write.Target, QueryStatus.Draft, cancellationToken))
        {
            throw new ContentAccessDeniedException(write.Type.Name, CorePermissions.OperationName(operation));
        }
    }

    // The shared row moved under the other locales, and each is Published only while its draft equals what is published.
    private async Task RecomputeOtherLocalesAsync(WriteScope write, List<Document> documents, Document document, CancellationToken cancellationToken)
    {
        if (!write.Type.Localized)
        {
            return;
        }

        foreach (var other in documents.Where(other => other.Id != document.Id && other.PublishedVersion is not null))
        {
            var draft = await store.ReadAsync(write.Type, other, ContentState.Draft, cancellationToken);
            var published = await store.ReadAsync(write.Type, other, ContentState.Published, cancellationToken);
            var status = SameFields(write.Type, draft, published) ? EntryStatus.Published : EntryStatus.Modified;
            if (status != other.Status)
            {
                await store.SetStatusAsync(other.DocumentId, other.Locale, status, cancellationToken);
            }
        }
    }

    private static bool SameFields(SchemaType type, ContentDocument left, ContentDocument right) =>
        JsonNode.DeepEquals(EntryJson.Fields(type, left), EntryJson.Fields(type, right));

    // A version as this principal may read it today: no hidden value at any depth, no field its grants leave out, no key
    // the schema has since lost, and no relation target it may not read. Hidden fields show to no one, the system principal included.
    private async Task<JsonObject> ReadableFieldsOfAsync(WriteScope write, ContentVersion version, CancellationToken cancellationToken)
    {
        var snapshot = ParseSnapshot(version);
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

        await DropUnreadableTargetsAsync(write, fields, cancellationToken);
        return fields;
    }

    // What a restore may take from a version. The system principal takes all of it; anyone else only what it could send
    // itself, so that an old version is no way past the limits of a write.
    private async Task<ContentDocument> RestorableFieldsAsync(WriteScope write, ContentVersion version, WritableFields writable, CancellationToken cancellationToken)
    {
        var isSystem = write.Permissions.IsSystem;
        var snapshot = ParseSnapshot(version);
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

        await DropUnreadableTargetsAsync(write, fields, cancellationToken);
        ContentDocument restored = [];
        foreach (var field in taken)
        {
            restored[field.Name] = fields[field.Name]?.DeepClone();
        }

        return restored;
    }

    private static JsonObject ParseSnapshot(ContentVersion version) =>
        JsonNode.Parse(version.Snapshot) as JsonObject ?? [];

    // A read does not name these targets, and a version does not either. Fails closed on one since trashed, which only a
    // draft read of the entry itself would show. References inside blocks and rich text stay as a draft read shows them.
    private async Task DropUnreadableTargetsAsync(WriteScope write, JsonObject fields, CancellationToken cancellationToken)
    {
        if (write.Permissions.IsSystem)
        {
            return;
        }

        foreach (var item in FieldStorage.Of(schema, write.Type).Where(item => item.Kind == FieldStorageKind.Reference))
        {
            if (schema.FindContentType(EntryPaths.ReferenceTarget(item.Field)) is not { } target)
            {
                continue;
            }

            var names = QueryPaths.ApiPath(write.Type, item).Split('.');
            var holder = fields;
            foreach (var name in names[..^1])
            {
                holder = holder?[name] as JsonObject;
            }

            if (holder is null)
            {
                continue;
            }

            switch (holder[names[^1]])
            {
                case JsonArray ids:
                    JsonArray kept = [];
                    foreach (var value in ids)
                    {
                        if (await CanReadTargetAsync(write, target, value, cancellationToken))
                        {
                            kept.Add(value?.DeepClone());
                        }
                    }

                    holder[names[^1]] = kept;
                    break;
                case { } single when !await CanReadTargetAsync(write, target, single, cancellationToken):
                    holder[names[^1]] = null;
                    break;
            }
        }
    }

    private async Task<bool> CanReadTargetAsync(WriteScope write, SchemaType target, JsonNode? value, CancellationToken cancellationToken) =>
        EntryCanonicalizer.TryReadId(RawValue.Unwrap(value), out var id)
            && await evaluator.CanAccessEntryAsync(write.Operation, target, ContentOperation.Read, id, write.Target, QueryStatus.Draft, cancellationToken);

    // A relation in the shape a read gives it, so that a version reads like the entry it was.
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
