using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content.Validation;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Hooks;
using FluentCMS.Jobs;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Content;

// Schedules (spec 4.5). A schedule is a row in cms_schedule and the job that runs it, written in one transaction, so there is
// never one without the other. The row is what counts: a job runs only if the row is still pending and names that job, so a
// cancelled or moved schedule is never run by the job it left behind. The job acts as the system principal for the principal
// that scheduled it, after checking again that this principal may still do what it asked.
internal sealed partial class ContentService
{
    public async Task<ScheduleInfo> ScheduleAsync(string type, Guid id, ScheduleRequest request, string? locale = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(request);

        var kind = request.Action == ScheduleAction.Unpublish ? ContentOperation.Unpublish : ContentOperation.Publish;
        var write = await BeginAsync(type, kind, cancellationToken, locale);

        // A schedule names who it acts for, and the anonymous visitor is nobody.
        if (write.Operation.AuditPrincipal.IsAnonymous)
        {
            throw new ContentAccessDeniedException(write.Type.Name, CorePermissions.OperationName(kind));
        }

        // Nothing here depends on the entry, so a request that is wrong reads the same whatever the id.
        var (runAt, zone) = ScheduleTime.Resolve(request, time.GetUtcNow());

        var document = await InTransactionAsync(
            async () =>
            {
                var (documents, document, target) = await ReachLocaleAsync(write, kind, id, cancellationToken);
                if (kind == ContentOperation.Publish)
                {
                    var draft = await store.ReadAsync(write.Type, document, ContentState.Draft, cancellationToken);
                    var published = await store.ReadAsync(write.Type, document, ContentState.Published, cancellationToken);
                    await CheckPublishGrantAsync(write, documents, document, target, draft, published, cancellationToken);
                    await CheckScheduledReferencesAsync(write, document, target, strict: false, cancellationToken);
                }
                else
                {
                    await CheckUnpublishGrantAsync(write, document, target, cancellationToken);
                }

                var action = ActionName(request.Action);
                var pending = await schedules.PendingAsync(id, document.Locale, action, cancellationToken);
                var row = new ScheduleRow
                {
                    Id = pending?.Id ?? Guid.CreateVersion7(),
                    DocumentId = id,
                    Locale = document.Locale,
                    Action = action,
                    RunAtUtc = runAt,
                    TimeZone = zone,
                    Version = document.DraftVersion,
                    CreatedBy = write.Operation.AuditPrincipal.Id,
                    CreatedAt = write.Operation.Timestamp,
                    Claims = ClaimsOf(write.Operation),
                };

                var payload = new ScheduledPayload(row.Id, ActorOf(write.Operation));
                var job = request.Action == ScheduleAction.Publish
                    ? jobs.Create<ScheduledPublishJob, ScheduledPayload>(payload, runAt, new JobOptions { Site = write.Site })
                    : jobs.Create<ScheduledUnpublishJob, ScheduledPayload>(payload, runAt, new JobOptions { Site = write.Site });
                await schedules.SaveAsync(pending, row, job, cancellationToken);
                await schedules.SetMirrorAsync(id, document.Locale, action, runAt, cancellationToken);

                _pending.Add(new EntryChange(EntryChangeKind.Scheduled, write.Type.Name, id, document.Locale, document.DraftVersion, write.Operation) { ScheduleAction = action, ScheduleAt = runAt });
                return document;
            },
            cancellationToken);

        return new ScheduleInfo(request.Action, document.Locale, runAt, zone, document.DraftVersion);
    }

    public async Task CancelScheduleAsync(string type, Guid id, ScheduleAction action, string? locale = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (!Enum.IsDefined(action))
        {
            throw new ValidationException([new ValidationError("action", ValidationErrorCodes.InvalidValue, "The action is not one that can be scheduled.")]);
        }

        var kind = action == ScheduleAction.Unpublish ? ContentOperation.Unpublish : ContentOperation.Publish;
        var write = await BeginAsync(type, kind, cancellationToken, locale);
        await InTransactionAsync(
            async () =>
            {
                var (documents, document, target) = await ReachLocaleAsync(write, kind, id, cancellationToken);
                if (kind == ContentOperation.Publish)
                {
                    var draft = await store.ReadAsync(write.Type, document, ContentState.Draft, cancellationToken);
                    var published = await store.ReadAsync(write.Type, document, ContentState.Published, cancellationToken);
                    await CheckPublishGrantAsync(write, documents, document, target, draft, published, cancellationToken);
                }
                else
                {
                    await CheckUnpublishGrantAsync(write, document, target, cancellationToken);
                }

                var cancelled = await schedules.CancelAsync(id, document.Locale, ActionName(action), write.Operation.Timestamp, cancellationToken)
                    ?? throw new ValidationException([new ValidationError("", ContentErrorCodes.NotScheduled, $"This {write.Type.Name} has no {ActionName(action)} scheduled in this locale.")]);

                _pending.Add(new EntryChange(EntryChangeKind.ScheduleCancelled, write.Type.Name, id, document.Locale, cancelled.Version, write.Operation) { ScheduleAction = cancelled.Action });
                return document;
            },
            cancellationToken);
    }

    // What the job runs. The schedule is claimed, the principal that scheduled it is checked again with the roles it has now,
    // and the entry is published or unpublished as the system principal, all in one transaction: whatever fails leaves the
    // entry as it was. A refusal is final and is kept on the schedule; any other failure is left to the runner's retries,
    // and recorded when the last one has been used.
    internal async Task<ScheduleOutcome> RunScheduledAsync(Guid scheduleId, Guid jobId, ScheduleAction action, bool lastAttempt, CancellationToken cancellationToken)
    {
        var operation = Current();
        var name = ActionName(action);
        if (await schedules.FindAsync(scheduleId, cancellationToken) is not { Status: ScheduleRow.Pending } row || row.JobId != jobId || row.Action != name)
        {
            return ScheduleOutcome.Superseded;
        }

        try
        {
            return await InTransactionAsync(() => RunClaimedAsync(operation, row, action, jobId, cancellationToken), cancellationToken);
        }
        catch (Exception exception) when ((IsRefusal(exception) || lastAttempt) && !(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            await schedules.CloseAsync(row.Id, jobId, ScheduleRow.Failed, time.GetUtcNow(), ErrorText(exception), CancellationToken.None);
            await schedules.SetMirrorAsync(row.DocumentId, row.Locale, row.Action, null, CancellationToken.None);
            if (!IsRefusal(exception))
            {
                throw;
            }

            ScheduleLog.Refused(logger, exception, row.Id, row.Action, row.DocumentId);
            return ScheduleOutcome.Failed;
        }
    }

    private async Task<ScheduleOutcome> RunClaimedAsync(OperationContext operation, ScheduleRow row, ScheduleAction action, Guid jobId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var state = await context.Documents.IgnoreQueryFilters().AsNoTracking()
            .Where(document => document.DocumentId == row.DocumentId && document.Locale == row.Locale)
            .Select(document => new { document.Type, document.DeletedAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (state is null || state.DeletedAt is not null)
        {
            return await EndAsync(row, jobId, ScheduleRow.Skipped, "The entry is in the trash.", cancellationToken);
        }

        var type = schema.FindContentType(state.Type)
            ?? throw new EntryNotFoundException(state.Type, row.DocumentId);
        var documents = await store.DocumentsAsync(type, row.DocumentId, operation.Site, trashed: false, cancellationToken);
        if (documents.Find(item => item.Locale == row.Locale) is not { } document)
        {
            return await EndAsync(row, jobId, ScheduleRow.Skipped, "The entry is not in this site.", cancellationToken);
        }

        // A publish takes the draft that was scheduled, not whatever it has become: someone else may have edited it since.
        if (action == ScheduleAction.Publish && document.DraftVersion != row.Version)
        {
            return await EndAsync(row, jobId, ScheduleRow.Skipped, $"{ContentErrorCodes.ScheduledDraftChanged}: the draft changed after it was scheduled.", cancellationToken);
        }

        if (action == ScheduleAction.Publish ? document.Status == EntryStatus.Published : document.PublishedVersion is null)
        {
            return await EndAsync(row, jobId, ScheduleRow.Skipped, action == ScheduleAction.Publish ? "The entry is already published." : "The entry is not published.", cancellationToken);
        }

        if (!await schedules.CloseAsync(row.Id, jobId, ScheduleRow.Executed, now, null, cancellationToken))
        {
            return ScheduleOutcome.Superseded;
        }

        await schedules.SetMirrorAsync(row.DocumentId, row.Locale, row.Action, null, cancellationToken);

        var locale = row.Locale.Length == 0 ? null : row.Locale;
        if (row.Claims is not null)
        {
            await CheckSchedulerAsync(operation, row, type, action, locale, cancellationToken);
        }

        if (action == ScheduleAction.Publish)
        {
            await PublishAsync(type.Name, row.DocumentId, locale, null, cancellationToken);
        }
        else
        {
            await UnpublishAsync(type.Name, row.DocumentId, locale, cancellationToken);
        }

        return ScheduleOutcome.Executed;
    }

    // The principal that scheduled it, as it is now: the roles it had are read again for the grants they carry today, so
    // a role that has lost the permission, or been deleted, no longer lets the schedule through. The system principal then
    // publishes, and sees every reference target, so a publish is first held to what this principal can see.
    private async Task CheckSchedulerAsync(OperationContext operation, ScheduleRow row, SchemaType type, ScheduleAction action, string? locale, CancellationToken cancellationToken)
    {
        if (operation.Actor is not { } actor || actor.IsSystem || actor.IsAnonymous)
        {
            throw new ContentAccessDeniedException(type.Name, CorePermissions.OperationName(action == ScheduleAction.Publish ? ContentOperation.Publish : ContentOperation.Unpublish));
        }

        var claims = JsonSerializer.Deserialize<Dictionary<string, string>>(row.Claims!) ?? [];
        var asScheduler = operation with { Principal = new Principal(actor.Kind, actor.Id, actor.Name, claims), Actor = null };
        var kind = action == ScheduleAction.Publish ? ContentOperation.Publish : ContentOperation.Unpublish;
        var write = await BeginAsync(asScheduler, type.Name, kind, locale, cancellationToken);
        var (documents, document, target) = await ReachLocaleAsync(write, kind, row.DocumentId, cancellationToken);
        if (action == ScheduleAction.Publish)
        {
            var draft = await store.ReadAsync(write.Type, document, ContentState.Draft, cancellationToken);
            var published = await store.ReadAsync(write.Type, document, ContentState.Published, cancellationToken);
            await CheckPublishGrantAsync(write, documents, document, target, draft, published, cancellationToken);
            await CheckScheduledReferencesAsync(write, document, target, strict: true, cancellationToken);
        }
        else
        {
            await CheckUnpublishGrantAsync(write, document, target, cancellationToken);
        }
    }

    // The reference rules of a publish that a system principal would not meet, asked as the principal that schedules. When it
    // is scheduled, a target that is not published yet is let through, since it may be by then; what the principal could never
    // reference is not. When it runs, everything counts.
    private async Task CheckScheduledReferencesAsync(WriteScope write, Document document, AccessTarget target, bool strict, CancellationToken cancellationToken)
    {
        if (write.Type.UnpublishedReferences != UnpublishedReferencePolicy.Block)
        {
            return;
        }

        var draft = await store.ReadAsync(write.Type, document, ContentState.Draft, cancellationToken);
        var check = new ValidationContext(write.Type, mapper.ValidationValues(write.Type, draft), write.Operation, ValidationMode.Publish);
        await CheckReferencesAsync(write, draft, document.DocumentId, target, check, [], cancellationToken, tolerateTargetState: !strict);

        var errors = new EntryErrors(schema, write.Type, write.Permissions, write.Target).ForPublish(check.Errors, EntryJson.Fields(write.Type, draft));
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    private async Task<ScheduleOutcome> EndAsync(ScheduleRow row, Guid jobId, string status, string reason, CancellationToken cancellationToken)
    {
        if (!await schedules.CloseAsync(row.Id, jobId, status, time.GetUtcNow(), reason, cancellationToken))
        {
            return ScheduleOutcome.Superseded;
        }

        await schedules.SetMirrorAsync(row.DocumentId, row.Locale, row.Action, null, cancellationToken);
        return ScheduleOutcome.Skipped;
    }

    private static string ActionName(ScheduleAction action) => action == ScheduleAction.Publish ? ScheduleRow.Publish : ScheduleRow.Unpublish;

    // The claims are what roles, scopes and row rules read of a principal. The roles in them are only where the grants are looked up from.
    private static string? ClaimsOf(OperationContext operation) =>
        operation.Principal.IsSystem ? null : JsonSerializer.Serialize(operation.Principal.Claims);

    private static JobActor? ActorOf(OperationContext operation) =>
        operation.AuditPrincipal is { IsSystem: false } actor ? new JobActor(actor.Kind, actor.Id, actor.Name) : null;

    private static bool IsRefusal(Exception exception) =>
        exception is ContentAccessDeniedException or EntryNotFoundException or ValidationException or HookRejectedException;

    // Codes and paths, never a message: a rule's message can quote a value of the entry.
    private static string ErrorText(Exception exception) => exception switch
    {
        ValidationException validation => string.Join("; ", validation.Errors.Select(error => $"{error.Code} {error.Path}".Trim())),
        HookRejectedException rejected => $"HookRejectedException {rejected.Code}",
        ContentAccessDeniedException or EntryNotFoundException => exception.Message,
        _ => exception.GetType().Name,
    };
}

internal enum ScheduleOutcome
{
    Executed,
    Skipped,
    Failed,
    Superseded,
}
