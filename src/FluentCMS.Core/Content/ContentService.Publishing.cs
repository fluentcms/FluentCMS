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
                await ThrowIfBeyondThePublishGrantAsync(write, id, target, draft, published, writable, cancellationToken);

                var original = EntryJson.Fields(write.Type, draft);
                var working = ContentCopy.Of(draft);
                await hooks.RunAsync(HookKind.BeforeValidate, write.Type, write.Operation, working, CopyOf(previous), cancellationToken);
                working = Settle(write, working, original, draft, writable);
                await ValidateForPublishAsync(write, working, document, target, warnings, cancellationToken);

                var validated = EntryJson.Fields(write.Type, working);
                await hooks.RunAsync(HookKind.BeforePublish, write.Type, write.Operation, working, CopyOf(previous), cancellationToken);
                working = Settle(write, working, validated, draft, writable);
                var settled = EntryJson.Fields(write.Type, working);
                if (!JsonNode.DeepEquals(validated, settled))
                {
                    await ValidateForPublishAsync(write, working, document, target, warnings, cancellationToken);
                }

                // A hook can call the service. What it saved, in another locale too, would go out with the shared row unvalidated.
                if (!SameFields(write.Type, draft, await store.ReadAsync(write.Type, document, ContentState.Draft, cancellationToken)))
                {
                    throw new HookRejectedException(ContentErrorCodes.DraftMovedByHook, "A hook changed the draft being published.");
                }

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

                var writable = await evaluator.WritableFieldsAsync(write.Operation, write.Type, ContentOperation.Update, id, target, cancellationToken);
                ThrowIfWritesBlind(write, writable);

                var stored = await store.ReadAsync(write.Type, document, ContentState.Draft, cancellationToken);
                var published = await store.ReadAsync(write.Type, document, ContentState.Published, cancellationToken);
                if (SameFields(write.Type, stored, published))
                {
                    if (document.Status != EntryStatus.Published)
                    {
                        await store.SetStatusAsync(id, document.Locale, EntryStatus.Published, cancellationToken);
                    }

                    return document;
                }

                var working = Prepare(write, EntryComposer.Replace(write.Type, stored, DiscardedValues(write, published, writable), static _ => true), document, stored);
                if (writable.Fields is not null)
                {
                    composer.CarryProtected(write.Type, stored, working, includingGroups: true);
                }

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
                ThrowIfWritesBlind(write, writable);

                var stored = await store.ReadAsync(write.Type, document, ContentState.Draft, cancellationToken);
                var restored = await RestorableFieldsAsync(write, version, writable, cancellationToken);

                var isSystem = write.Permissions.IsSystem;
                var composed = EntryComposer.Replace(write.Type, stored, restored, field => !isSystem && Keeps(write, field, writable, addsLocale: false));
                var working = Prepare(write, composed, document, stored);
                if (!isSystem)
                {
                    var shown = await ReadShownAsync(write, id, documents, addsLocale: false, cancellationToken);
                    composer.CarryProtected(write.Type, stored, working, includingGroups: true, await UnseenNestedTargetsAsync(write, stored, shown, cancellationToken));
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

    // A discard or restore answers with a status and a version that depend on whether the draft equals what it is compared
    // with. Where the principal can write a field it cannot read, that would check a saved guess, so it is refused whatever the field holds.
    private static void ThrowIfWritesBlind(WriteScope write, WritableFields writable)
    {
        if (!write.Permissions.IsSystem && write.Type.Fields.Any(field => !field.Hidden && !field.ReadOnly && writable.Includes(field.Name) && !write.Permissions.CanReadField(write.Type, field.Name, write.Target)))
        {
            throw new ContentAccessDeniedException(write.Type.Name, CorePermissions.OperationName(ContentOperation.Update));
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
    private ContentDocument Settle(WriteScope write, ContentDocument working, JsonObject baseline, ContentDocument stored, WritableFields writable)
    {
        var canonical = canonicalizer.Canonicalize(write.Type, working, static (_, _) => false, stored);
        var fields = EntryJson.Fields(write.Type, canonical);
        write.Access.RejectHookChanges(write.Type.Fields.Select(field => field.Name).Where(name => !JsonNode.DeepEquals(baseline[name], fields[name])), write.Type, writable);
        return canonical;
    }

    private async Task ValidateForPublishAsync(WriteScope write, ContentDocument working, Document document, AccessTarget target, List<PublishWarning> warnings, CancellationToken cancellationToken)
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
                await CheckReferencesAsync(write, working, document.DocumentId, target, context, warnings, token);
            });

        var errors = new EntryErrors(schema, write.Type, write.Permissions, write.Target).ForPublish(result.Errors, EntryJson.Fields(write.Type, working));
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    // A write takes any id without looking, so a warning or a refusal that told a missing target from a published one would
    // be a way to ask about any entry. What a principal could have chosen is therefore never confirmed to it (spec 4.5).
    // A self-reference is exempt: it is what is being published.
    private async Task CheckReferencesAsync(WriteScope write, ContentDocument working, Guid id, AccessTarget entryTarget, ValidationContext context, List<PublishWarning> warnings, CancellationToken cancellationToken)
    {
        var blocks = write.Type.UnpublishedReferences == UnpublishedReferencePolicy.Block;
        var locales = (await fallback.ChainAsync(write.Site, write.Locale, LocaleFallback.Chain, cancellationToken)).Locales;
        var isSystem = write.Permissions.IsSystem;
        HashSet<(string Path, string Code, Guid? Id)> told = [];
        WritableFields? updatable = null;
        WritableFields? creatable = null;

        void Refuse() => context.AddError("", PublishWarningCodes.UnavailableReference, "The entry refers to an entry the principal cannot confirm.");

        foreach (var reference in store.TargetsOf(write.Type, working))
        {
            if (reference.Id == id && reference.TargetType == write.Type.Name)
            {
                continue;
            }

            var target = schema.FindContentType(reference.TargetType);
            if (!isSystem && !(reference.IsShown && write.Permissions.CanReadField(write.Type, reference.Field, write.Target)))
            {
                if (blocks)
                {
                    updatable ??= await evaluator.WritableFieldsAsync(write.Operation, write.Type, ContentOperation.Update, id, entryTarget, cancellationToken);
                    creatable ??= await evaluator.WritableFieldsAsync(write.Operation, write.Type, ContentOperation.Create, id, entryTarget, cancellationToken);
                    var chose = reference.IsShown && (updatable.Includes(reference.Field) || creatable.Includes(reference.Field));
                    if (chose || !await IsPublishedTargetAsync(write, target, reference.Id, locales, cancellationToken))
                    {
                        Refuse();
                    }
                }

                continue;
            }

            if (!isSystem && (target is null || !write.Permissions.Allows(target, ContentOperation.Read, write.Target)))
            {
                if (blocks)
                {
                    Refuse();
                }

                continue;
            }

            var state = await TargetStateAsync(write, target, reference.Id, locales, cancellationToken);
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

    private async Task<bool> IsPublishedTargetAsync(WriteScope write, SchemaType? target, Guid id, IReadOnlyList<string> locales, CancellationToken cancellationToken) =>
        target is not null && IsPublishedIn(target, await store.TargetRowsAsync(target, id, write.Site, cancellationToken), locales);

    private static bool IsPublishedIn(SchemaType target, List<ReferenceTargetRow> rows, IReadOnlyList<string> locales) =>
        rows.Any(row => row.IsPublished && (!target.Localized || locales.Contains(row.Locale, StringComparer.OrdinalIgnoreCase)));

    // What a principal that may read the type may know of a target. A row only in locales its grant does not cover, and a
    // row its rule refuses, read the same as no row. The rule is asked of the locale that serves the target, not the entry's.
    private async Task<ReferenceTargetState> TargetStateAsync(WriteScope write, SchemaType? target, Guid id, IReadOnlyList<string> locales, CancellationToken cancellationToken)
    {
        if (target is null)
        {
            return ReferenceTargetState.Unavailable;
        }

        var rows = await store.TargetRowsAsync(target, id, write.Site, cancellationToken);
        if (!write.Permissions.IsSystem)
        {
            rows = [.. rows.Where(row => !target.Localized || write.Permissions.Allows(target, ContentOperation.Read, write.Target with { Locale = row.Locale }))];
        }

        if (rows.Count == 0)
        {
            return ReferenceTargetState.Unavailable;
        }

        if (!write.Permissions.IsSystem)
        {
            var serving = locales.Select(locale => rows.Find(row => row.IsPublished && string.Equals(row.Locale, locale, StringComparison.OrdinalIgnoreCase))).FirstOrDefault(row => row is not null)
                ?? locales.Select(locale => rows.Find(row => string.Equals(row.Locale, locale, StringComparison.OrdinalIgnoreCase))).FirstOrDefault(row => row is not null)
                ?? rows[0];
            var asked = target.Localized ? write.Target with { Locale = serving.Locale } : write.Target;
            if (!await evaluator.CanAccessEntryAsync(write.Operation, target, ContentOperation.Read, id, asked, QueryStatus.Draft, cancellationToken))
            {
                return ReferenceTargetState.Unavailable;
            }
        }

        return IsPublishedIn(target, rows, locales) ? ReferenceTargetState.Published : ReferenceTargetState.Unpublished;
    }

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

    // Hidden fields show to no one, the system principal included.
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

    // Anyone but the system principal takes only what it could send itself, so that an old version is no way past the limits of a write.
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

    // Fails closed on a target since trashed, which only a draft read of the entry itself would show.
    private async Task DropUnreadableTargetsAsync(WriteScope write, JsonObject fields, CancellationToken cancellationToken)
    {
        if (write.Permissions.IsSystem)
        {
            return;
        }

        var nested = new NestedTargets(schema);
        HashSet<(string Type, Guid Id)> readable = [];
        foreach (var (name, ids) in nested.Collect(write.Type, fields))
        {
            if (schema.FindContentType(name) is not { } target || !write.Permissions.Allows(target, ContentOperation.Read, write.Target))
            {
                continue;
            }

            foreach (var id in ids)
            {
                if (await evaluator.CanAccessEntryAsync(write.Operation, target, ContentOperation.Read, id, write.Target, QueryStatus.Draft, cancellationToken))
                {
                    readable.Add((name, id));
                }
            }
        }

        nested.Filter(write.Type, fields, (name, id) => readable.Contains((name, id)));
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
