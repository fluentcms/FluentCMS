using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content.Hooks;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Core.Transfer;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using FluentCMS.Transfer;

namespace FluentCMS.Core.Content;

// Import mode (spec 4.14): the import of a transfer package writes an entry as the system under Origin.Import, through the
// same steps as any write, so validation, hooks, references, versions and events behave as they always do. What it adds is
// the id of the package, and once the writes are done the times, the authors and the version numbers of the package. Each
// entry is one transaction, so an import that stopped is finished by running it again.
internal sealed partial class ContentService
{
    private IReadOnlySet<(string Target, Guid Id)>? ProvidedTargets => importMode is { Active: true, DryRun: true } ? importMode.Provided : null;

    // Skipped when every locale of the package is in the instance as the package has it, a conflict when the instance's
    // copy of a locale is newer, unless overwrite. Anything else is written, and refused as any write would be.
    internal async Task<TransferOutcome> ImportAsync(string typeName, TransferEntry entry, bool overwrite, CancellationToken cancellationToken)
    {
        using var span = Span("import", typeName);

        ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(entry);

        var operation = Current();
        if (!importMode.Active || operation.Origin != Origin.Import || !operation.Principal.IsSystem)
        {
            throw new InvalidOperationException("Only the import of a transfer package writes in import mode.");
        }

        var type = schema.FindContentType(typeName)
            ?? throw new ValidationException([new ValidationError("type", QueryErrorCodes.UnknownField, $"'{typeName}' is not a content type.")]);

        var rows = await store.AllRowsAsync(entry.Id, cancellationToken);
        if (rows.Any(row => row.Type != type.Name || (type.Scope == ContentScope.Site && row.SiteId != operation.Site.Value)))
        {
            throw new ValidationException([new ValidationError("id", TransferErrorCodes.IdTaken, $"The id {entry.Id:D} is that of an entry of another type or site of this instance.")]);
        }

        if (rows.Any(row => row.DeletedAt is not null))
        {
            throw new ValidationException([new ValidationError("id", TransferErrorCodes.InTrash, $"The entry {entry.Id:D} is in the trash of this instance; restore or purge it first.")]);
        }

        if (rows.Count > 0)
        {
            var matched = entry.Locales.Select(locale => (Package: locale, Row: rows.Find(row => row.Locale == locale.Locale))).ToList();
            if (matched.All(pair => pair.Row is { } row && SameMoment(row.UpdatedAt, pair.Package.UpdatedAt) && row.Status == pair.Package.Status))
            {
                return TransferOutcome.Skipped;
            }

            if (!overwrite && matched.Any(pair => pair.Row is { } row && Microseconds(row.UpdatedAt) > Microseconds(pair.Package.UpdatedAt)))
            {
                return TransferOutcome.Conflict;
            }
        }

        try
        {
            await InTransactionAsync(
                async () =>
                {
                    await WriteImportedAsync(type, entry, rows, cancellationToken);
                    return importMode.DryRun ? throw new DryRunRollback() : true;
                },
                cancellationToken);
        }
        catch (DryRunRollback)
        {
            // Everything the entry would have written, its events and outbox rows included, went with the transaction.
        }

        return rows.Count == 0 ? TransferOutcome.Created : TransferOutcome.Updated;
    }

    // The published states go first, each written as the draft and published, so that a locale whose draft has moved on is
    // then written again with its draft. A locale the package does not publish is unpublished.
    private async Task WriteImportedAsync(SchemaType type, TransferEntry entry, List<Document> before, CancellationToken cancellationToken)
    {
        foreach (var locale in entry.Locales.Where(locale => locale.Published is not null))
        {
            await WriteImportedDraftAsync(type, entry.Id, locale.Locale, locale.Published!, cancellationToken);
            await PublishAsync(type.Name, entry.Id, LocaleOf(type, locale), expectedVersion: null, cancellationToken);
        }

        foreach (var locale in entry.Locales.Where(locale => locale.Published is null))
        {
            if (before.Find(row => row.Locale == locale.Locale)?.PublishedVersion is not null)
            {
                await UnpublishAsync(type.Name, entry.Id, LocaleOf(type, locale), cancellationToken);
            }
        }

        foreach (var locale in entry.Locales)
        {
            if (locale.Published is null || !JsonNode.DeepEquals(locale.DraftFields, locale.Published))
            {
                await WriteImportedDraftAsync(type, entry.Id, locale.Locale, locale.DraftFields, cancellationToken);
            }
        }

        var written = await store.DocumentsAsync(type, entry.Id, Current().Site, trashed: false, cancellationToken);
        foreach (var locale in entry.Locales)
        {
            var row = written.Find(item => item.Locale == locale.Locale)!;
            var (draftVersion, publishedVersion) = before.Count == 0
                ? await RenumberImportedAsync(row, locale, cancellationToken)
                : (row.DraftVersion, row.PublishedVersion);
            // Never published or published no more: the package says which, since an unpublish leaves Unpublished either way.
            var status = row.PublishedVersion is null && locale.Status is EntryStatus.Draft or EntryStatus.Unpublished ? locale.Status : row.Status;
            await store.SettleImportedAsync(
                entry.Id,
                row.Locale,
                new ImportedState(
                    status,
                    draftVersion,
                    publishedVersion,
                    locale.CreatedAt,
                    locale.CreatedBy,
                    locale.UpdatedAt,
                    locale.UpdatedBy,
                    publishedVersion is null ? null : locale.PublishedAt ?? row.PublishedAt,
                    locale.FirstPublishedAt ?? row.FirstPublishedAt),
                cancellationToken);
        }
    }

    // An entry the import created has only the versions the import wrote, which take the numbers of the package: the
    // published one the package's published version and the last its draft version, the ones before each counting down
    // from it. When the numbers cannot be laid out so, as when a hook wrote more versions, the entry keeps its own.
    private async Task<(int Draft, int? Published)> RenumberImportedAsync(Document row, TransferEntryLocale locale, CancellationToken cancellationToken)
    {
        var numbers = await store.VersionNumbersAsync(row.DocumentId, row.Locale, cancellationToken);
        if (numbers.Count == 0 || (row.PublishedVersion is null) != (locale.PublishedVersion is null))
        {
            return (row.DraftVersion, row.PublishedVersion);
        }

        Dictionary<int, int> moves = [];
        foreach (var number in numbers)
        {
            moves[number] = row.PublishedVersion is { } published && number <= published
                ? locale.PublishedVersion!.Value - (published - number)
                : locale.DraftVersion - (row.DraftVersion - number);
        }

        var ordered = numbers.Select(number => moves[number]).ToList();
        var valid = ordered[0] >= 1 && ordered.Zip(ordered.Skip(1)).All(pair => pair.First < pair.Second);
        if (!valid || moves.All(move => move.Key == move.Value))
        {
            return valid ? (locale.DraftVersion, locale.PublishedVersion) : (row.DraftVersion, row.PublishedVersion);
        }

        await store.RenumberVersionsAsync(row.DocumentId, row.Locale, moves, cancellationToken);
        return (locale.DraftVersion, locale.PublishedVersion);
    }

    // A draft of one locale, as an update by the system would write it: every field of the package, hidden and read-only ones
    // too, and a new entry under the id of the package. A field the package does not have, as when the instance has more,
    // keeps what the entry holds, or takes its default on a new entry.
    private async Task WriteImportedDraftAsync(SchemaType type, Guid id, string locale, JsonObject fields, CancellationToken cancellationToken)
    {
        var documents = await store.DocumentsAsync(type, id, Current().Site, trashed: false, cancellationToken);
        var isNewEntry = documents.Count == 0;
        var write = await BeginAsync(type.Name, isNewEntry ? ContentOperation.Create : ContentOperation.Update, cancellationToken, type.Localized ? locale : null);
        if (isNewEntry && type.Kind == SchemaTypeKind.Singleton && await store.AnyEntryAsync(type, write.Site, cancellationToken))
        {
            throw new ValidationException([new ValidationError("", ContentErrorCodes.SingletonExists, $"{type.Name} is a singleton and already has its entry.")]);
        }

        var existing = documents.Find(item => item.Locale == write.DocumentLocale);
        var document = existing ?? new Document
        {
            DocumentId = id,
            Locale = write.DocumentLocale,
            Type = type.Name,
            SiteId = isNewEntry ? write.DocumentSite : documents[0].SiteId,
            Status = EntryStatus.Draft,
            DraftVersion = 0,
            CreatedAt = write.Operation.Timestamp,
            CreatedBy = isNewEntry ? write.Operation.AuditPrincipal.Id : documents[0].CreatedBy,
            UpdatedAt = write.Operation.Timestamp,
            UpdatedBy = write.Operation.AuditPrincipal.Id,
        };

        var target = isNewEntry
            ? write.Target
            : write.Target with { EntryLocales = [.. documents.Select(item => item.Locale).Append(write.DocumentLocale).Distinct(StringComparer.OrdinalIgnoreCase)], Found = true };
        var writable = await evaluator.WritableFieldsAsync(write.Operation, type, isNewEntry ? ContentOperation.Create : ContentOperation.Update, isNewEntry ? null : id, target, cancellationToken);
        var draft = isNewEntry ? null : await store.ReadDraftAsync(type, document, cancellationToken);
        var stored = draft?.Entry;

        ContentDocument sent = [];
        foreach (var field in type.Fields.Where(field => fields.ContainsKey(field.Name)))
        {
            sent[field.Name] = fields[field.Name]?.DeepClone();
        }

        ContentDocument composed = stored is null ? new ContentDocument(sent) : EntryComposer.Replace(type, stored, sent, field => !fields.ContainsKey(field.Name));
        foreach (var field in type.Fields.Where(field => stored is null && field.DefaultValue is not null && !composed.ContainsKey(field.Name)))
        {
            composed[field.Name] = field.DefaultValue;
        }

        var working = Prepare(write, composed, document, stored);
        (working, var written) = await RunBeforeHooksAsync(write, isNewEntry ? HookKind.BeforeCreate : HookKind.BeforeUpdate, working, new SaveContext(stored, Client(sent), writable, document), cancellationToken);

        var status = document.Status;
        if (document.PublishedVersion is not null)
        {
            var published = document.Status == EntryStatus.Published ? stored! : await store.ReadAsync(type, document, ContentState.Published, cancellationToken);
            status = SameFields(type, written, published) ? EntryStatus.Published : EntryStatus.Modified;
        }

        if (existing is null)
        {
            document.DraftVersion = 1;
        }
        else if (await store.AdvanceVersionAsync(id, document.Locale, document.DraftVersion, status, write.Operation.Timestamp, write.Operation.AuditPrincipal.Id, cancellationToken))
        {
            document.DraftVersion++;
            document.Status = status;
            document.UpdatedAt = write.Operation.Timestamp;
            document.UpdatedBy = write.Operation.AuditPrincipal.Id;
        }
        else
        {
            throw await ConflictAsync(write, id, document.DraftVersion, await store.DraftVersionAsync(id, document.Locale, cancellationToken), cancellationToken);
        }

        await store.WriteDraftAsync(type, document, isNewDocument: existing is null, draft?.Rows ?? StoredRows.None, working, written, write.Operation.Origin, write.Operation.ClientId, cancellationToken);
        List<EntryChange> skipped = [];
        if (!isNewEntry)
        {
            await RecomputeOtherLocalesAsync(write, documents, document, cancellationToken);
            skipped = await SkipStalePublishesAsync(write, existing is null ? [.. documents, document] : documents, document, cancellationToken);
        }

        EntryStore.ApplySystem(working, document);
        await hooks.RunAsync(isNewEntry ? HookKind.AfterCreate : HookKind.AfterUpdate, type, write.Operation, working, CopyOf(stored), cancellationToken);
        _pending.Add(new EntryChange(EntryChangeKind.DraftSaved, type.Name, id, document.Locale, document.DraftVersion, write.Operation));
        _pending.AddRange(skipped);
    }

    private static string? LocaleOf(SchemaType type, TransferEntryLocale locale) => type.Localized ? locale.Locale : null;

    // To the microsecond, the finest that every provider keeps, so that a package moved between providers is the same again.
    private static long Microseconds(DateTimeOffset moment) => moment.UtcTicks / 10;

    private static bool SameMoment(DateTimeOffset left, DateTimeOffset right) => Microseconds(left) == Microseconds(right);

    // Thrown at the end of a dry run's transaction, so that the transaction takes back what the entry wrote.
    private sealed class DryRunRollback : Exception
    {
        public DryRunRollback()
            : base("The dry run of an import writes nothing.")
        {
        }
    }
}
