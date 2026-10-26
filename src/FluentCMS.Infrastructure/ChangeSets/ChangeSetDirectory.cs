using System.Text.Json;
using FluentCMS.Core.Agents;
using FluentCMS.Core.Audit;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Content;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Media;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Core.Rules;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Media;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.ChangeSets;

// Expected failures are found before the unit of work begins, so they leave a caller's outer unit of work able to commit. An agent's
// daily cap is the exception: it is counted inside, so concurrent opens take turns, and its refusal rolls the caller's unit of work back.
// Reads never track rows, and updates go straight to the database, so a long-lived context always sees what is stored.
internal sealed class ChangeSetDirectory(
    FluentCmsDbContext context,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    ICurrentInitiator currentInitiator,
    IAgentPermissions permissions,
    IEnumerable<IChangeSetEvents> events) : IChangeSetDirectory
{
    // AgentActivity counts these events for the change-sets-per-day cap.
    internal const string OpenedOperation = "change_set.opened";

    private const string SubjectType = "change_set";

    // Until multi-site (MST-01), every set belongs to the one site (SCH-07).
    private static readonly Guid SiteId = Site.Default.Id;

    public async Task<Result<ChangeSet>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var stored = await ReadAsync(id, cancellationToken);
        if (stored is null)
        {
            return NotFound(id);
        }

        var readable = await permissions.CheckChangeSetReadAsync(stored.ChangeSet, cancellationToken);
        return readable.IsSuccess ? Result<ChangeSet>.Success(stored.ChangeSet) : Result<ChangeSet>.Failure(readable.Errors);
    }

    public async Task<Result<ChangeSetDiff>> DiffAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var stored = await ReadAsync(id, cancellationToken);
        if (stored is null)
        {
            return Result<ChangeSetDiff>.Failure(NotFound(id).Errors);
        }

        var readable = await permissions.CheckChangeSetReadAsync(stored.ChangeSet, cancellationToken);
        if (!readable.IsSuccess)
        {
            return Result<ChangeSetDiff>.Failure(readable.Errors);
        }

        var set = stored.ChangeSet;
        var definitions = await DefinitionsAsync(set.Operations, cancellationToken);
        var rules = await RuleDefinitionsAsync(set.Operations, cancellationToken);
        var entries = await EntryDiffsAsync(set, cancellationToken);
        var media = await MediaDiffsAsync(set, cancellationToken);

        var items = set.Operations
            .Select((operation, position) => operation.ItemKind switch
            {
                ItemKind.Schema => ItemDiff.ForSchema(
                    operation,
                    Definition(definitions, set.Id, operation.ItemId, operation.BaseVersion),
                    Definition(definitions, set.Id, operation.ItemId, operation.ProposedVersion)),
                ItemKind.Entry or ItemKind.Entity => entries[position],
                ItemKind.Media => media[position],
                ItemKind.Rule => ItemDiff.ForRule(
                    operation,
                    RuleDefinitionAt(rules, set.Id, operation.ItemId, operation.BaseVersion),
                    RuleDefinitionAt(rules, set.Id, operation.ItemId, operation.ProposedVersion)),
                _ => throw new InvalidOperationException(
                    $"Change set {set.Id} holds an operation on a {ItemSubjectType(operation.ItemKind)} item, which no diff covers yet."),
            })
            .ToList();

        return Result<ChangeSetDiff>.Success(new ChangeSetDiff(set.Id, items));
    }

    public async Task<Result<ChangeSet>> GetSessionSetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        var openId = await OpenSetOfSessionAsync(Initiator(), sessionId, cancellationToken);
        return openId is { } id && await ReadAsync(id, cancellationToken) is { } stored
            ? Result<ChangeSet>.Success(stored.ChangeSet)
            : Result<ChangeSet>.Failure(ChangeSetErrorCodes.ChangeSetNotFound, $"You have no open change set for session '{sessionId}'.");
    }

    public async Task<Result<ChangeSet>> OpenAsync(NewChangeSet changeSet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changeSet);

        var opened = ChangeSet.Open(SiteId, changeSet.Rationale, changeSet.Sources, Initiator());
        if (!opened.IsSuccess)
        {
            return opened;
        }

        // Counted inside the unit of work, after holding the agent's credential, so concurrent opens cannot all pass the cap.
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var allowed = await permissions.CheckNewChangeSetAsync(cancellationToken);
        if (!allowed.IsSuccess)
        {
            return Result<ChangeSet>.Failure(allowed.Errors);
        }

        await InsertAsync(opened.Value, cancellationToken);

        return await CommitAsync(scope, opened.Value, Conflict(opened.Value.Id), cancellationToken);
    }

    public async Task<Result<ChangeSet>> AddOperationAsync(Guid changeSetId, ChangeOperation operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var stored = await ReadAsync(changeSetId, cancellationToken);

        return stored is null ? NotFound(changeSetId) : await AddAsync(stored, operation, [], cancellationToken);
    }

    public async Task<Result<ChangeSet>> AddToSessionAsync(
        string sessionId,
        NewChangeSet changeSet,
        ChangeOperation operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(changeSet);
        ArgumentNullException.ThrowIfNull(operation);

        var initiator = Initiator();
        var openId = await OpenSetOfSessionAsync(initiator, sessionId, cancellationToken);
        if (openId is not null && await ReadAsync(openId.Value, cancellationToken) is { } stored)
        {
            return await AddAsync(stored, operation, changeSet.Sources, cancellationToken);
        }

        var opened = ChangeSet.Open(SiteId, changeSet.Rationale, changeSet.Sources, initiator, sessionId);
        if (!opened.IsSuccess)
        {
            return opened;
        }

        var set = opened.Value;

        var added = set.AddOperation(operation);
        var previousVersion = added.IsSuccess ? await ResolveAsync(operation, cancellationToken) : Result<Guid?>.Failure(added.Errors);
        if (!previousVersion.IsSuccess)
        {
            return Result<ChangeSet>.Failure(previousVersion.Errors);
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var allowed = await permissions.CheckNewChangeSetAsync(cancellationToken);
        if (!allowed.IsSuccess)
        {
            return Result<ChangeSet>.Failure(allowed.Errors);
        }

        await InsertAsync(set, cancellationToken);
        await InsertOperationAsync(set, set.Operations.Count - 1, previousVersion.Value, cancellationToken);

        // A concurrent first operation of the same session that committed first trips the one-open-set index.
        return await CommitAsync(
            scope,
            set,
            Result<ChangeSet>.Failure(
                ChangeSetErrorCodes.Conflict,
                $"Another call opened a change set for session '{sessionId}' at the same time; retry and the operation joins it."),
            cancellationToken);
    }

    public Task<Result<ChangeSet>> TransitionAsync(Guid changeSetId, ChangeSetState target, CancellationToken cancellationToken = default)
    {
        // No call moves a set to published or reverted without its items.
        return target switch
        {
            ChangeSetState.Published => PublishAsync(changeSetId, cancellationToken),
            ChangeSetState.Reverted => MoveAsync(changeSetId, target, _ => Result.Failure(
                ChangeSetErrorCodes.TransitionNotAllowed,
                "A change set is reverted by publishing the change set that reverting it opens; revert it instead."), cancellationToken),
            _ => MoveAsync(changeSetId, target, set => set.TransitionTo(target), cancellationToken),
        };
    }

    public async Task<Result<ChangeSet>> PublishAsync(Guid changeSetId, CancellationToken cancellationToken = default)
    {
        var stored = await ReadAsync(changeSetId, cancellationToken);
        if (stored is null)
        {
            return NotFound(changeSetId);
        }

        var allowed = await permissions.CheckTransitionAsync(stored.ChangeSet, ChangeSetState.Published, cancellationToken);
        if (!allowed.IsSuccess)
        {
            return Result<ChangeSet>.Failure(allowed.Errors);
        }

        var set = stored.ChangeSet;
        var moved = set.TransitionTo(ChangeSetState.Published);
        if (!moved.IsSuccess)
        {
            return Result<ChangeSet>.Failure(moved.Errors);
        }

        var items = await PublishableItemsAsync(set, cancellationToken);
        if (!items.IsSuccess)
        {
            return Result<ChangeSet>.Failure(items.Errors);
        }

        // A revert moves the set it undoes to reverted in the same unit of work (CHG-06).
        var reverted = set.RevertsChangeSetId is { } revertedId ? await ReadAsync(revertedId, cancellationToken) : null;
        if (reverted is not null && !reverted.ChangeSet.TransitionTo(ChangeSetState.Reverted).IsSuccess)
        {
            return Result<ChangeSet>.Failure(
                ChangeSetErrorCodes.TransitionNotAllowed,
                $"This change set reverts change set {reverted.ChangeSet.Id}, which is no longer published, so there is nothing left to revert.");
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        try
        {
            return await PublishInScopeAsync(scope, stored, reverted, items.Value, cancellationToken);
        }
        catch (Exception exception) when (DatabaseErrors.IsConcurrencyFailure(exception))
        {
            // The scope rolls back when it is disposed.
            return Conflict(set.Id);
        }
    }

    private async Task<Result<ChangeSet>> PublishInScopeAsync(
        IUnitOfWorkScope scope,
        StoredChangeSet stored,
        StoredChangeSet? reverted,
        IReadOnlyList<PublishableItem> items,
        CancellationToken cancellationToken)
    {
        var set = stored.ChangeSet;
        if (!await UpdateAsync(stored.Revision, set, cancellationToken))
        {
            return Conflict(set.Id);
        }

        if (reverted is not null && !await UpdateAsync(reverted.Revision, reverted.ChangeSet, cancellationToken))
        {
            return Conflict(reverted.ChangeSet.Id);
        }

        // Items move by kind and id, whatever order the set lists them in, so two sets that publish the same items take their row
        // locks in the same order and one waits for the other instead of deadlocking. The events keep the set's order.
        foreach (var item in items.OrderBy(item => item.Operation.ItemKind).ThenBy(item => item.Operation.ItemId))
        {
            // The scope rolls back the items moved before this one.
            if (!await MovePublishedVersionAsync(item.Operation, set.RevertsChangeSetId is not null, cancellationToken))
            {
                return Result<ChangeSet>.Failure(
                    ChangeSetErrorCodes.BaseVersionStale,
                    $"Another change set published {item.Name} at the same time, so it is no longer at the version this change set was based on.");
            }
        }

        foreach (var item in items)
        {
            var subjectType = ItemSubjectType(item.Operation.ItemKind);
            await auditLog.AppendAsync(
                new NewAuditEvent(item.Operation.Kind == OperationKind.Delete ? $"{subjectType}.unpublished" : $"{subjectType}.published")
                {
                    SubjectType = subjectType,
                    SubjectId = item.Operation.ItemId,
                    ChangeSetId = set.Id,
                    Sources = set.Sources,
                    PreviousVersionId = item.PreviousVersionId,
                },
                cancellationToken);
        }

        await AppendTransitionAsync(set, cancellationToken);
        if (reverted is not null)
        {
            await AppendTransitionAsync(reverted.ChangeSet, cancellationToken);
        }

        // Listeners such as webhooks queue their work in this unit of work, so it commits with the publish or not at all.
        foreach (var listener in events)
        {
            await listener.PublishedAsync(set, reverted?.ChangeSet, cancellationToken);
        }

        return await CommitAsync(scope, set, Conflict(set.Id), cancellationToken);
    }

    public async Task<Result<ChangeSet>> RevertAsync(Guid changeSetId, string rationale, CancellationToken cancellationToken = default)
    {
        var stored = await ReadAsync(changeSetId, cancellationToken);
        if (stored is null)
        {
            return NotFound(changeSetId);
        }

        // Reverting changes what is live, as publishing does, so only a person reverts, and people have no caps (MCP-06).
        var allowed = await permissions.CheckTransitionAsync(stored.ChangeSet, ChangeSetState.Reverted, cancellationToken);
        if (!allowed.IsSuccess)
        {
            return Result<ChangeSet>.Failure(allowed.Errors);
        }

        var opened = stored.ChangeSet.Revert(rationale, Initiator());
        if (!opened.IsSuccess)
        {
            return opened;
        }

        // The revert publishes over the versions the original published, so an item that moved on since fails it here already.
        var revert = opened.Value;
        var items = await PublishableItemsAsync(revert, cancellationToken);
        if (!items.IsSuccess)
        {
            return Result<ChangeSet>.Failure(items.Errors);
        }


        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        await InsertAsync(revert, cancellationToken);
        for (var position = 0; position < revert.Operations.Count; position++)
        {
            await InsertOperationAsync(revert, position, items.Value[position].PreviousVersionId, cancellationToken);
        }

        return await CommitAsync(scope, revert, Conflict(revert.Id), cancellationToken);
    }

    public Task<Result<ChangeSet>> RejectAsync(Guid changeSetId, string reason, CancellationToken cancellationToken = default)
    {
        return MoveAsync(changeSetId, ChangeSetState.Rejected, set => set.Reject(reason), cancellationToken);
    }

    // A write of an item in another directory that is also an operation of a change set, such as a content type revision
    // (SCH-05), joins its set in two steps, so the item's rows and the operation commit together. PrepareAsync finds every
    // expected failure before the caller's unit of work begins; BeginWriteAsync and RecordAsync run inside it, around the item's own
    // rows and event.
    // ItemPublished says whether a change set has published the item, so an update of one that is not live records a create.
    internal async Task<Result<PendingOperation>> PrepareAsync(
        ChangeSetTarget target,
        ChangeOperation operation,
        bool itemPublished,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(operation);

        if (target.ChangeSetId is { } id)
        {
            var stored = await ReadAsync(id, cancellationToken);
            return stored is null
                ? Result<PendingOperation>.Failure(NotFound(id).Errors)
                : await PrepareJoinAsync(stored, operation, itemPublished, [], cancellationToken);
        }

        var sessionId = target.SessionId!;
        var initiator = Initiator();
        var openId = await OpenSetOfSessionAsync(initiator, sessionId, cancellationToken);
        if (openId is not null && await ReadAsync(openId.Value, cancellationToken) is { } open)
        {
            return await PrepareJoinAsync(open, operation, itemPublished, target.NewChangeSet!.Sources, cancellationToken);
        }

        var opened = ChangeSet.Open(SiteId, target.NewChangeSet!.Rationale, target.NewChangeSet.Sources, initiator, sessionId);
        if (!opened.IsSuccess)
        {
            return Result<PendingOperation>.Failure(opened.Errors);
        }

        var set = opened.Value;
        var recorded = set.Record(operation, itemPublished);
        if (!recorded.IsSuccess)
        {
            return Result<PendingOperation>.Failure(recorded.Errors);
        }

        // The daily cap is counted when the set is written, inside the caller's unit of work, as for OpenAsync.
        return Result<PendingOperation>.Success(new PendingOperation(set, null, 0, Extends: false));
    }

    // The version the set a write joins already proposes for the item, which a later write in that set builds on (SCH-05). Null when
    // that set holds no operation proposing a version of the item, the write would open the set, or the set is not open, which the
    // join then refuses.
    internal async Task<int?> ProposedVersionAsync(ChangeSetTarget target, ItemKind itemKind, Guid itemId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        var setId = target.ChangeSetId ?? await OpenSetOfSessionAsync(Initiator(), target.SessionId!, cancellationToken);
        if (setId is null)
        {
            return null;
        }

        return await (
                from operation in context.ChangeSetOperations.AsNoTracking()
                join set in context.ChangeSets.AsNoTracking() on operation.ChangeSetId equals set.Id
                where set.Id == setId
                    && set.State == ChangeSetState.Open
                    && operation.ItemKind == itemKind
                    && operation.ItemId == itemId
                select operation.ProposedVersion)
            .SingleOrDefaultAsync(cancellationToken);
    }

    // Whether a set that can still publish, one not yet published, rejected or reverted, proposes this version of the item.
    internal Task<bool> ProposesAsync(ItemKind itemKind, Guid itemId, int version, CancellationToken cancellationToken)
    {
        return LiveOperations(itemKind)
            .AnyAsync(operation => operation.ItemId == itemId && operation.ProposedVersion == version, cancellationToken);
    }

    // The operations of sets that can still publish, for a caller's query to join.
    internal IQueryable<ChangeSetOperationRow> LiveOperations(ItemKind itemKind)
    {
        return
            from operation in context.ChangeSetOperations.AsNoTracking()
            join set in context.ChangeSets.AsNoTracking() on operation.ChangeSetId equals set.Id
            where set.SiteId == SiteId
                && operation.ItemKind == itemKind
                && set.State != ChangeSetState.Published
                && set.State != ChangeSetState.Rejected
                && set.State != ChangeSetState.Reverted
            select operation;
    }

    // The first step inside the caller's unit of work, before the item's own rows and event: it opens the set or moves its revision.
    // It fails when an agent's daily cap refuses the set the write opens, counted here with the agent's credential held so concurrent
    // opens take turns, or when another call changed the set since PrepareAsync read it. Either way the caller rolls back.
    internal async Task<Result> BeginWriteAsync(PendingOperation pending, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pending);

        var set = pending.ChangeSet;
        if (pending.Revision is { } revision)
        {
            return await UpdateAsync(revision, set, cancellationToken) ? Result.Success() : Result.Failure(Conflict(set.Id).Errors);
        }

        var allowed = await permissions.CheckNewChangeSetAsync(cancellationToken);
        if (!allowed.IsSuccess)
        {
            return allowed;
        }

        await InsertAsync(set, cancellationToken);
        return Result.Success();
    }

    // The last step, after the item's own rows and event: the operation, with the row of the version the write was based on.
    internal Task RecordAsync(PendingOperation pending, Guid? previousVersionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pending);

        return pending.Extends
            ? ExtendOperationAsync(pending.ChangeSet, pending.Position, previousVersionId, cancellationToken)
            : InsertOperationAsync(pending.ChangeSet, pending.Position, previousVersionId, cancellationToken);
    }

    private async Task<Result<PendingOperation>> PrepareJoinAsync(
        StoredChangeSet stored,
        ChangeOperation operation,
        bool itemPublished,
        IReadOnlyList<string> sources,
        CancellationToken cancellationToken)
    {
        var set = stored.ChangeSet;
        var count = set.Operations.Count;

        var recorded = set.Record(operation, itemPublished);
        if (!recorded.IsSuccess)
        {
            return Result<PendingOperation>.Failure(recorded.Errors);
        }

        var sourcesAdded = set.AddSources(sources);
        if (!sourcesAdded.IsSuccess)
        {
            return Result<PendingOperation>.Failure(sourcesAdded.Errors);
        }

        // Checked against the read whose revision guards the write, as for AddOperationAsync.
        var allowed = await permissions.CheckChangeSetSizeAsync(set, cancellationToken: cancellationToken);
        if (!allowed.IsSuccess)
        {
            return Result<PendingOperation>.Failure(allowed.Errors);
        }

        var position = set.Operations.ToList().FindIndex(existing => existing.ItemKind == operation.ItemKind && existing.ItemId == operation.ItemId);
        return Result<PendingOperation>.Success(new PendingOperation(set, stored.Revision, position, Extends: set.Operations.Count == count));
    }

    private async Task<Result<ChangeSet>> AddAsync(
        StoredChangeSet stored,
        ChangeOperation operation,
        IReadOnlyList<string> sources,
        CancellationToken cancellationToken)
    {
        var set = stored.ChangeSet;

        // An operation that moves the item's operation on to a newer version takes its place rather than joining the set.
        var position = set.Operations.ToList().FindIndex(existing => existing.ItemKind == operation.ItemKind && existing.ItemId == operation.ItemId);
        var added = set.AddOperation(operation);
        if (!added.IsSuccess)
        {
            return Result<ChangeSet>.Failure(added.Errors);
        }


        var sourcesAdded = set.AddSources(sources);
        if (!sourcesAdded.IsSuccess)
        {
            return Result<ChangeSet>.Failure(sourcesAdded.Errors);
        }

        var previousVersion = await ResolveAsync(operation, cancellationToken);
        if (!previousVersion.IsSuccess)
        {
            return Result<ChangeSet>.Failure(previousVersion.Errors);
        }

        // Checked against the read whose revision guards the write, so two concurrent operations cannot both pass the cap.
        var allowed = await permissions.CheckChangeSetSizeAsync(set, cancellationToken: cancellationToken);
        if (!allowed.IsSuccess)
        {
            return Result<ChangeSet>.Failure(allowed.Errors);
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        if (!await UpdateAsync(stored.Revision, set, cancellationToken))
        {
            return Conflict(set.Id);
        }

        if (position >= 0)
        {
            await ExtendOperationAsync(set, position, previousVersion.Value, cancellationToken);
        }
        else
        {
            await InsertOperationAsync(set, set.Operations.Count - 1, previousVersion.Value, cancellationToken);
        }

        return await CommitAsync(scope, set, Conflict(set.Id), cancellationToken);
    }

    private async Task<Result<ChangeSet>> MoveAsync(
        Guid changeSetId,
        ChangeSetState target,
        Func<ChangeSet, Result> move,
        CancellationToken cancellationToken)
    {
        var stored = await ReadAsync(changeSetId, cancellationToken);
        if (stored is null)
        {
            return NotFound(changeSetId);
        }

        var allowed = await permissions.CheckTransitionAsync(stored.ChangeSet, target, cancellationToken);
        if (!allowed.IsSuccess)
        {
            return Result<ChangeSet>.Failure(allowed.Errors);
        }

        var set = stored.ChangeSet;
        var moved = move(set);
        if (!moved.IsSuccess)
        {
            return Result<ChangeSet>.Failure(moved.Errors);
        }

        // A reopened set groups its session's operations again, so the session must not have another open set (CHG-02).
        if (set.State == ChangeSetState.Open
            && set.SessionId is not null
            && await OpenSetOfSessionAsync(set.Initiator, set.SessionId, cancellationToken) is { } other)
        {
            return Result<ChangeSet>.Failure(
                ChangeSetErrorCodes.SessionHasOpenSet,
                $"Session '{set.SessionId}' already has the open change set {other}; submit it before reopening this one.");
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        if (!await UpdateAsync(stored.Revision, set, cancellationToken))
        {
            return Conflict(set.Id);
        }

        await AppendTransitionAsync(set, cancellationToken);

        return await CommitAsync(scope, set, Conflict(set.Id), cancellationToken);
    }

    private Task AppendTransitionAsync(ChangeSet set, CancellationToken cancellationToken)
    {
        return auditLog.AppendAsync(
            new NewAuditEvent(TransitionOperation(set.State)) { SubjectType = SubjectType, SubjectId = set.Id, ChangeSetId = set.Id },
            cancellationToken);
    }

    private async Task InsertAsync(ChangeSet set, CancellationToken cancellationToken)
    {
        var (kind, initiatorId) = InitiatorKinds.Describe(set.Initiator);

        context.ChangeSets.Add(new ChangeSetRow
        {
            Id = set.Id,
            SiteId = set.SiteId,
            Rationale = set.Rationale,
            Sources = [.. set.Sources],
            InitiatorKind = kind,
            InitiatorId = initiatorId,
            InitiatorName = set.Initiator.DisplayName,
            SessionId = set.SessionId,
            State = set.State,
            RevertsChangeSetId = set.RevertsChangeSetId,
        });

        await auditLog.AppendAsync(
            new NewAuditEvent(OpenedOperation) { SubjectType = SubjectType, SubjectId = set.Id, ChangeSetId = set.Id, Sources = set.Sources },
            cancellationToken);
    }

    // An operation of the set, with the stored version it was based on as the event's previous version (AUD-01).
    private async Task InsertOperationAsync(ChangeSet set, int position, Guid? previousVersionId, CancellationToken cancellationToken)
    {
        var operation = set.Operations[position];

        context.ChangeSetOperations.Add(new ChangeSetOperationRow
        {
            Id = Guid.CreateVersion7(),
            ChangeSetId = set.Id,
            Position = position,
            Kind = operation.Kind,
            ItemKind = operation.ItemKind,
            ItemId = operation.ItemId,
            BaseVersion = operation.BaseVersion,
            ProposedVersion = operation.ProposedVersion,
        });

        await auditLog.AppendAsync(
            new NewAuditEvent("change_set.operation_added")
            {
                SubjectType = ItemSubjectType(operation.ItemKind),
                SubjectId = operation.ItemId,
                ChangeSetId = set.Id,
                Sources = set.Sources,
                PreviousVersionId = previousVersionId,
            },
            cancellationToken);
    }

    // A later write of an item already in the set moves its operation to the write's version, with the version the write was
    // based on as the event's previous version (AUD-01).
    private async Task ExtendOperationAsync(ChangeSet set, int position, Guid? previousVersionId, CancellationToken cancellationToken)
    {
        var operation = set.Operations[position];

        await context.ChangeSetOperations
            .Where(row => row.ChangeSetId == set.Id && row.Position == position)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ProposedVersion, operation.ProposedVersion), cancellationToken);

        await auditLog.AppendAsync(
            new NewAuditEvent("change_set.operation_updated")
            {
                SubjectType = ItemSubjectType(operation.ItemKind),
                SubjectId = operation.ItemId,
                ChangeSetId = set.Id,
                Sources = set.Sources,
                PreviousVersionId = previousVersionId,
            },
            cancellationToken);
    }

    // Writes only over the revision this call read, so a concurrent writer that committed first leaves nothing to update.
    private async Task<bool> UpdateAsync(int revision, ChangeSet set, CancellationToken cancellationToken)
    {
        var sources = set.Sources.ToList();
        var updated = await context.ChangeSets
            .Where(row => row.Id == set.Id && row.Revision == revision)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(row => row.Revision, revision + 1)
                    .SetProperty(row => row.State, set.State)
                    .SetProperty(row => row.RejectionReason, set.RejectionReason)
                    .SetProperty(row => row.Sources, sources),
                cancellationToken);

        return updated == 1;
    }

    // Points an operation at stored versions of its item; the result is the id of the base version's row, if any.
    private async Task<Result<Guid?>> ResolveAsync(ChangeOperation operation, CancellationToken cancellationToken)
    {
        if (operation.ItemKind is ItemKind.Entry or ItemKind.Entity)
        {
            return await ResolveEntryAsync(operation, cancellationToken);
        }

        if (operation.ItemKind == ItemKind.Media)
        {
            return await ResolveMediaAsync(operation, cancellationToken);
        }

        if (operation.ItemKind == ItemKind.Rule)
        {
            return await ResolveRuleAsync(operation, cancellationToken);
        }

        if (operation.ItemKind != ItemKind.Schema)
        {
            return Result<Guid?>.Failure(
                ChangeSetErrorCodes.ItemKindUnavailable,
                $"There are no stored {ItemSubjectType(operation.ItemKind)} items yet, so a change set cannot point at one; "
                + "only schema, entry, entity, media and rule operations can be added.");
        }

        var slug = await context.ContentTypes
            .AsNoTracking()
            .Where(type => type.Id == operation.ItemId && type.SiteId == SiteId)
            .Select(type => type.Slug)
            .SingleOrDefaultAsync(cancellationToken);
        if (slug is null)
        {
            return Result<Guid?>.Failure(ChangeSetErrorCodes.ItemNotFound, $"There is no content type {operation.ItemId} in this site.");
        }

        // A schema operation proposes a definition, so the scope must allow its operation on the whole type (MCP-05).
        var allowed = await permissions.CheckDefinitionAsync(slug, AgentOperationOf(operation.Kind), cancellationToken);
        if (!allowed.IsSuccess)
        {
            return Result<Guid?>.Failure(allowed.Errors);
        }

        var wanted = new[] { operation.BaseVersion, operation.ProposedVersion }.OfType<int>().ToList();
        var stored = await context.ContentTypeVersions
            .AsNoTracking()
            .Where(version => version.ContentTypeId == operation.ItemId && wanted.Contains(version.Version))
            .Select(version => new { version.Version, version.Id })
            .ToListAsync(cancellationToken);

        var missing = wanted.Except(stored.Select(version => version.Version)).ToList();
        if (missing.Count > 0)
        {
            return Result<Guid?>.Failure(missing.Select(version => new Error(
                ChangeSetErrorCodes.VersionNotFound,
                $"The content type '{slug}' has no version {version}.")));
        }

        return Result<Guid?>.Success(stored.SingleOrDefault(version => version.Version == operation.BaseVersion)?.Id);
    }

    // A rule operation names a rule of the site, at versions it has. Only a person writes rules, so an agent's operation on one is
    // refused here as the rule directory refuses its writes.
    private async Task<Result<Guid?>> ResolveRuleAsync(ChangeOperation operation, CancellationToken cancellationToken)
    {
        if (currentInitiator.Current is AgentInitiator or SystemInitiator)
        {
            return Result<Guid?>.Failure(RuleErrorCodes.PeopleOnly, "Only a person can put a rule in a change set.");
        }

        var slug = await context.Rules
            .AsNoTracking()
            .Where(rule => rule.Id == operation.ItemId && rule.SiteId == SiteId)
            .Select(rule => rule.Slug)
            .SingleOrDefaultAsync(cancellationToken);
        if (slug is null)
        {
            return Result<Guid?>.Failure(ChangeSetErrorCodes.ItemNotFound, $"There is no rule {operation.ItemId} in this site.");
        }

        var wanted = new[] { operation.BaseVersion, operation.ProposedVersion }.OfType<int>().ToList();
        var stored = await context.RuleVersions
            .AsNoTracking()
            .Where(version => version.RuleId == operation.ItemId && wanted.Contains(version.Version))
            .Select(version => new { version.Version, version.Id })
            .ToListAsync(cancellationToken);

        var missing = wanted.Except(stored.Select(version => version.Version)).ToList();
        if (missing.Count > 0)
        {
            return Result<Guid?>.Failure(missing.Select(version => new Error(ChangeSetErrorCodes.VersionNotFound, $"The rule '{slug}' has no version {version}.")));
        }

        return Result<Guid?>.Success(stored.SingleOrDefault(version => version.Version == operation.BaseVersion)?.Id);
    }

    // Every stored definition the rule operations name, keyed by rule and version, read in one query.
    private async Task<Dictionary<(Guid RuleId, int Version), string>> RuleDefinitionsAsync(
        IReadOnlyList<ChangeOperation> operations,
        CancellationToken cancellationToken)
    {
        var rules = operations.Where(operation => operation.ItemKind == ItemKind.Rule).ToList();
        if (rules.Count == 0)
        {
            return [];
        }

        var ids = rules.Select(operation => operation.ItemId).Distinct().ToList();
        var versions = rules.SelectMany(operation => new[] { operation.BaseVersion, operation.ProposedVersion }).OfType<int>().Distinct().ToList();
        var rows = await context.RuleVersions
            .AsNoTracking()
            .Where(version => ids.Contains(version.RuleId) && versions.Contains(version.Version))
            .Select(version => new { version.RuleId, version.Version, version.Definition })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => (row.RuleId, row.Version), row => row.Definition);
    }

    // Versions are never deleted and an operation is checked against them when it is added, so a missing one is a broken store.
    private static RuleDefinition? RuleDefinitionAt(Dictionary<(Guid RuleId, int Version), string> definitions, Guid changeSetId, Guid ruleId, int? version)
    {
        if (version is not { } number)
        {
            return null;
        }

        if (!definitions.TryGetValue((ruleId, number), out var json))
        {
            throw new InvalidOperationException($"Change set {changeSetId} points at version {number} of rule {ruleId}, which is not stored.");
        }

        var definition = RuleDefinition.Parse(json);
        return definition.IsSuccess
            ? definition.Value
            : throw new InvalidOperationException($"Version {number} of rule {ruleId} cannot be read: {definition.Errors[0].Message}");
    }

    // The rules a set's operations name, with their active version and the row of each version an operation is based on.
    private async Task<Dictionary<Guid, PublishedRule>> PublishedRulesAsync(IReadOnlyList<ChangeOperation> operations, CancellationToken cancellationToken)
    {
        var rules = operations.Where(operation => operation.ItemKind == ItemKind.Rule).ToList();
        if (rules.Count == 0)
        {
            return [];
        }

        var ids = rules.Select(operation => operation.ItemId).Distinct().ToList();
        var bases = rules.Select(operation => operation.BaseVersion).OfType<int>().Distinct().ToList();
        var rows = await context.Rules
            .AsNoTracking()
            .Where(rule => rule.SiteId == SiteId && ids.Contains(rule.Id))
            .Select(rule => new { rule.Id, rule.Slug, rule.ActiveVersion })
            .ToListAsync(cancellationToken);
        var versions = await context.RuleVersions
            .AsNoTracking()
            .Where(version => ids.Contains(version.RuleId) && bases.Contains(version.Version))
            .Select(version => new { version.RuleId, version.Version, version.Id })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            row => row.Id,
            row => new PublishedRule(row.Slug, row.ActiveVersion, versions.Where(version => version.RuleId == row.Id).ToDictionary(version => version.Version, version => version.Id)));
    }

    private static PublishableItem? PublishableRule(ChangeSet set, ChangeOperation operation, Dictionary<Guid, PublishedRule> rules, List<Error> errors)
    {
        // Rules are never deleted and an operation is checked against them when it is added.
        var rule = rules.GetValueOrDefault(operation.ItemId)
            ?? throw new InvalidOperationException($"Change set {set.Id} points at rule {operation.ItemId}, which is not stored.");

        if (rule.ActiveVersion != operation.BaseVersion)
        {
            var active = rule.ActiveVersion is { } current ? $"is active at version {current}" : "has no active version";
            var based = operation.BaseVersion is { } number ? $"was based on version {number}" : "makes it active";
            errors.Add(new Error(ChangeSetErrorCodes.BaseVersionStale, $"The rule '{rule.Slug}' {active}, but this change set {based}."));
            return null;
        }

        Guid? previousVersionId = operation.BaseVersion is { } baseVersion ? rule.Versions[baseVersion] : null;
        return new PublishableItem(operation, $"the rule '{rule.Slug}'", previousVersionId);
    }

    private sealed record PublishedRule(string Slug, int? ActiveVersion, Dictionary<int, Guid> Versions);

    // Every stored definition the schema operations name, keyed by content type and version, read in one query.
    private async Task<Dictionary<(Guid ContentTypeId, int Version), string>> DefinitionsAsync(
        IReadOnlyList<ChangeOperation> operations,
        CancellationToken cancellationToken)
    {
        var schema = operations.Where(operation => operation.ItemKind == ItemKind.Schema).ToList();
        if (schema.Count == 0)
        {
            return [];
        }

        var ids = schema.Select(operation => operation.ItemId).Distinct().ToList();
        var versions = schema.SelectMany(operation => new[] { operation.BaseVersion, operation.ProposedVersion }).OfType<int>().Distinct().ToList();

        var rows = await context.ContentTypeVersions
            .AsNoTracking()
            .Where(version => ids.Contains(version.ContentTypeId) && versions.Contains(version.Version))
            .Select(version => new { version.ContentTypeId, version.Version, version.Definition })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => (row.ContentTypeId, row.Version), row => row.Definition);
    }

    // Versions are never deleted and an operation is checked against them when it is added, so a missing one is a broken store.
    private static ContentTypeDefinition? Definition(
        Dictionary<(Guid ContentTypeId, int Version), string> definitions,
        Guid changeSetId,
        Guid contentTypeId,
        int? version)
    {
        if (version is not { } number)
        {
            return null;
        }

        if (!definitions.TryGetValue((contentTypeId, number), out var json))
        {
            throw new InvalidOperationException(
                $"Change set {changeSetId} points at version {number} of content type {contentTypeId}, which is not stored.");
        }

        var definition = ContentTypeDefinition.Parse(json);

        return definition.IsSuccess
            ? definition.Value
            : throw new InvalidOperationException(
                $"Version {number} of content type {contentTypeId} is stored in a form that cannot be read: "
                + string.Join("; ", definition.Errors.Select(error => error.Message)));
    }

    // The items of a set with the row of the version each is published at. Every item must still be published at its
    // operation's base version, and a create's item not at all, so a publish never replaces a version its set has not seen.
    private async Task<Result<List<PublishableItem>>> PublishableItemsAsync(ChangeSet set, CancellationToken cancellationToken)
    {
        if (set.Operations.FirstOrDefault(operation => operation.ItemKind is not (ItemKind.Schema or ItemKind.Entry or ItemKind.Entity or ItemKind.Media or ItemKind.Rule)) is { } other)
        {
            throw new InvalidOperationException(
                $"Change set {set.Id} holds an operation on a {ItemSubjectType(other.ItemKind)} item, which nothing publishes yet.");
        }

        var entries = await PublishedEntriesAsync(set.Operations, cancellationToken);
        var media = await PublishedMediaAsync(set.Operations, cancellationToken);
        var rules = await PublishedRulesAsync(set.Operations, cancellationToken);

        var ids = set.Operations.Select(operation => operation.ItemId).ToList();
        var bases = set.Operations.Select(operation => operation.BaseVersion).OfType<int>().Distinct().ToList();

        var types = await context.ContentTypes
            .AsNoTracking()
            .Where(type => type.SiteId == SiteId && ids.Contains(type.Id))
            .Select(type => new { type.Id, type.Slug, type.PublishedVersion })
            .ToDictionaryAsync(type => type.Id, cancellationToken);
        var versions = await context.ContentTypeVersions
            .AsNoTracking()
            .Where(version => ids.Contains(version.ContentTypeId) && bases.Contains(version.Version))
            .Select(version => new { version.ContentTypeId, version.Version, version.Id })
            .ToDictionaryAsync(version => (version.ContentTypeId, version.Version), version => version.Id, cancellationToken);

        var items = new List<PublishableItem>();
        var errors = new List<Error>();
        foreach (var operation in set.Operations)
        {
            if (operation.ItemKind is ItemKind.Entry or ItemKind.Entity or ItemKind.Media)
            {
                if (PublishableEntry(set, operation, operation.ItemKind == ItemKind.Media ? media : entries, errors) is { } item)
                {
                    items.Add(item);
                }

                continue;
            }

            if (operation.ItemKind == ItemKind.Rule)
            {
                if (PublishableRule(set, operation, rules, errors) is { } item)
                {
                    items.Add(item);
                }

                continue;
            }

            // Types and versions are never deleted and an operation is checked against them when it is added.
            var type = types.GetValueOrDefault(operation.ItemId)
                ?? throw new InvalidOperationException($"Change set {set.Id} points at content type {operation.ItemId}, which is not stored.");

            if (type.PublishedVersion != operation.BaseVersion)
            {
                var published = type.PublishedVersion is { } current ? $"is published at version {current}" : "is not published";
                var based = operation.BaseVersion is { } number ? $"was based on version {number}" : "creates it";
                errors.Add(new Error(ChangeSetErrorCodes.BaseVersionStale, $"The content type '{type.Slug}' {published}, but this change set {based}."));
                continue;
            }

            Guid? previousVersionId = operation.BaseVersion is { } baseVersion ? versions[(operation.ItemId, baseVersion)] : null;
            items.Add(new PublishableItem(operation, $"the content type '{type.Slug}'", previousVersionId));
        }

        return errors.Count == 0 ? Result<List<PublishableItem>>.Success(items) : Result<List<PublishableItem>>.Failure(errors);
    }

    // Moves only from the base version, so a publish over the same item that committed first leaves nothing to move.
    private async Task<bool> MovePublishedVersionAsync(ChangeOperation operation, bool revert, CancellationToken cancellationToken)
    {
        if (operation.ItemKind == ItemKind.Media)
        {
            // A delete published from here unpublishes the asset; its versions and its bytes stay.
            return await context.MediaAssets
                .Where(asset => asset.Id == operation.ItemId && asset.PublishedVersion == operation.BaseVersion)
                .ExecuteUpdateAsync(setters => setters.SetProperty(asset => asset.PublishedVersion, operation.ProposedVersion), cancellationToken) == 1;
        }

        if (operation.ItemKind is ItemKind.Entry or ItemKind.Entity)
        {
            // A delete published from here unpublishes the entry; its versions and its draft stay. The state moves with the pointer,
            // and nothing else moves it (CNT-01).
            var state = EntryStates.AfterPublish(operation.Kind, revert);
            return await context.Entries
                .Where(entry => entry.Id == operation.ItemId && entry.PublishedVersion == operation.BaseVersion && entry.DeletedAt == null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(entry => entry.PublishedVersion, operation.ProposedVersion).SetProperty(entry => entry.State, state),
                    cancellationToken) == 1;
        }

        if (operation.ItemKind == ItemKind.Rule)
        {
            // A retirement published from here retires the rule, and a revert of the retirement makes it active again. A revert of the
            // rule's creation puts it back as the draft it was before that set, since no set retired it (RUL-03). Its versions stay.
            var state = operation.ProposedVersion is not null ? RuleState.Active : revert ? RuleState.Draft : RuleState.Retired;
            return await context.Rules
                .Where(rule => rule.Id == operation.ItemId && rule.ActiveVersion == operation.BaseVersion)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(rule => rule.ActiveVersion, operation.ProposedVersion).SetProperty(rule => rule.State, state),
                    cancellationToken) == 1;
        }

        var moved = await context.ContentTypes
            .Where(type => type.Id == operation.ItemId && type.PublishedVersion == operation.BaseVersion)
            .ExecuteUpdateAsync(setters => setters.SetProperty(type => type.PublishedVersion, operation.ProposedVersion), cancellationToken);

        return moved == 1;
    }

    // An entry operation points at an entry of a plain content type and an entity operation at an entry of an entity type, so the
    // desk can tell content changes from fact changes (CHG-09). A deleted entry waits for its restore.
    private async Task<Result<Guid?>> ResolveEntryAsync(ChangeOperation operation, CancellationToken cancellationToken)
    {
        var entry = await (
            from row in context.Entries.AsNoTracking()
            join type in context.ContentTypes.AsNoTracking() on row.ContentTypeId equals type.Id
            join current in context.ContentTypeVersions.AsNoTracking()
                on new { ContentTypeId = type.Id, type.Version } equals new { current.ContentTypeId, current.Version }
            where row.Id == operation.ItemId && row.SiteId == SiteId
            select new { row.DeletedAt, type.Slug, current.Definition })
            .SingleOrDefaultAsync(cancellationToken);

        if (entry is null)
        {
            return Result<Guid?>.Failure(ChangeSetErrorCodes.ItemNotFound, $"There is no entry {operation.ItemId} in this site.");
        }

        if (entry.DeletedAt is not null)
        {
            return Result<Guid?>.Failure(
                ChangeSetErrorCodes.ItemNotFound,
                $"Entry {operation.ItemId} is deleted; restore it before a change set points at it.");
        }

        var isEntity = ReadDefinition($"of the content type '{entry.Slug}'", entry.Definition).IsEntity;
        if (isEntity != (operation.ItemKind == ItemKind.Entity))
        {
            return Result<Guid?>.Failure(
                ChangeSetErrorCodes.ItemKindMismatch,
                isEntity
                    ? $"Entry {operation.ItemId} is an entity of the entity type '{entry.Slug}', so an entity operation changes it."
                    : $"Entry {operation.ItemId} is of the content type '{entry.Slug}', which is not an entity type, so an entry operation changes it.");
        }

        // The write that stored the version was checked field by field; the operation is checked on the type (MCP-05).
        var allowed = await permissions.CheckContentAsync(entry.Slug, AgentOperationOf(operation.Kind), cancellationToken: cancellationToken);
        if (!allowed.IsSuccess)
        {
            return Result<Guid?>.Failure(allowed.Errors);
        }

        var wanted = new[] { operation.BaseVersion, operation.ProposedVersion }.OfType<int>().ToList();
        var stored = await context.EntryVersions
            .AsNoTracking()
            .Where(version => version.EntryId == operation.ItemId && wanted.Contains(version.Version))
            .Select(version => new { version.Version, version.Id })
            .ToListAsync(cancellationToken);

        var missing = wanted.Except(stored.Select(version => version.Version)).ToList();
        if (missing.Count > 0)
        {
            return Result<Guid?>.Failure(missing.Select(version => new Error(
                ChangeSetErrorCodes.VersionNotFound,
                $"Entry {operation.ItemId} has no version {version}.")));
        }

        return Result<Guid?>.Success(stored.SingleOrDefault(version => version.Version == operation.BaseVersion)?.Id);
    }

    // The diff of every entry and entity operation, by its position in the set, from all the versions they name in one query. Each
    // version brings the definition it was written against, so fields keep their order on both databases.
    private async Task<Dictionary<int, ItemDiff>> EntryDiffsAsync(ChangeSet set, CancellationToken cancellationToken)
    {
        var operations = set.Operations
            .Select((operation, position) => (Operation: operation, Position: position))
            .Where(item => item.Operation.ItemKind is ItemKind.Entry or ItemKind.Entity)
            .ToList();
        if (operations.Count == 0)
        {
            return [];
        }

        var ids = operations.Select(item => item.Operation.ItemId).Distinct().ToList();
        var numbers = operations
            .SelectMany(item => new[] { item.Operation.BaseVersion, item.Operation.ProposedVersion })
            .OfType<int>()
            .Distinct()
            .ToList();

        var rows = await (
            from version in context.EntryVersions.AsNoTracking()
            join entry in context.Entries.AsNoTracking() on version.EntryId equals entry.Id
            join written in context.ContentTypeVersions.AsNoTracking()
                on new { entry.ContentTypeId, Version = version.ContentTypeVersion } equals new { written.ContentTypeId, written.Version }
            where ids.Contains(version.EntryId) && numbers.Contains(version.Version)
            select new { version.EntryId, version.Version, version.Fields, written.Definition })
            .ToListAsync(cancellationToken);
        var versions = rows.ToDictionary(row => (row.EntryId, row.Version));

        // Versions are never changed and an operation is checked against them when it is added, so a missing one is a broken store.
        (JsonElement Fields, string Definition)? Stored(Guid entryId, int? number)
        {
            if (number is not { } version)
            {
                return null;
            }

            var row = versions.GetValueOrDefault((entryId, version))
                ?? throw new InvalidOperationException($"Change set {set.Id} points at version {version} of entry {entryId}, which is not stored.");

            return (JsonSerializer.Deserialize<JsonElement>(row.Fields), row.Definition);
        }

        return operations.ToDictionary(
            item => item.Position,
            item =>
            {
                var before = Stored(item.Operation.ItemId, item.Operation.BaseVersion);
                var after = Stored(item.Operation.ItemId, item.Operation.ProposedVersion);
                var shown = (after ?? before)!.Value;

                return ItemDiff.ForEntry(item.Operation, before?.Fields, after?.Fields, ReadDefinition($"of entry {item.Operation.ItemId}", shown.Definition));
            });
    }

    // Each entry the operations name with its published version, whether it is deleted, and the rows of their base versions.
    private async Task<Dictionary<Guid, PublishedEntry>> PublishedEntriesAsync(IReadOnlyList<ChangeOperation> operations, CancellationToken cancellationToken)
    {
        var entryOperations = operations.Where(operation => operation.ItemKind is ItemKind.Entry or ItemKind.Entity).ToList();
        if (entryOperations.Count == 0)
        {
            return [];
        }

        var ids = entryOperations.Select(operation => operation.ItemId).Distinct().ToList();
        var bases = entryOperations.Select(operation => operation.BaseVersion).OfType<int>().Distinct().ToList();

        var entries = await context.Entries
            .AsNoTracking()
            .Where(entry => entry.SiteId == SiteId && ids.Contains(entry.Id))
            .Select(entry => new { entry.Id, entry.PublishedVersion, Deleted = entry.DeletedAt != null })
            .ToListAsync(cancellationToken);
        var versions = await context.EntryVersions
            .AsNoTracking()
            .Where(version => ids.Contains(version.EntryId) && bases.Contains(version.Version))
            .Select(version => new { version.EntryId, version.Version, version.Id })
            .ToListAsync(cancellationToken);

        var versionIds = versions.ToLookup(version => version.EntryId);

        return entries.ToDictionary(
            entry => entry.Id,
            entry => new PublishedEntry(
                entry.PublishedVersion,
                entry.Deleted,
                versionIds[entry.Id].ToDictionary(version => version.Version, version => version.Id)));
    }

    // An entry is published only at its operation's base version, and a create's entry not at all; a deleted one waits for its
    // restore. Entries are never removed inside the retention window, and an operation is checked against them when it is added.
    private static PublishableItem? PublishableEntry(
        ChangeSet set,
        ChangeOperation operation,
        Dictionary<Guid, PublishedEntry> entries,
        List<Error> errors)
    {
        var name = $"the {ItemSubjectType(operation.ItemKind)} {operation.ItemId}";
        var entry = entries.GetValueOrDefault(operation.ItemId)
            ?? throw new InvalidOperationException($"Change set {set.Id} points at {name}, which is not stored.");

        if (entry.Deleted)
        {
            errors.Add(new Error(ChangeSetErrorCodes.ItemNotFound, $"{Capitalize(name)} is deleted, so it cannot be published; restore it first."));
            return null;
        }

        if (entry.PublishedVersion != operation.BaseVersion)
        {
            var published = entry.PublishedVersion is { } current ? $"is published at version {current}" : "is not published";
            var based = operation.BaseVersion is { } number ? $"was based on version {number}" : "creates it";
            errors.Add(new Error(ChangeSetErrorCodes.BaseVersionStale, $"{Capitalize(name)} {published}, but this change set {based}."));
            return null;
        }

        return new PublishableItem(operation, name, operation.BaseVersion is { } baseVersion ? entry.VersionIds[baseVersion] : null);
    }

    // A media operation points at an asset of the site and at stored versions of it (MED-03); an agent needs its scope to allow the
    // operation on media.
    private async Task<Result<Guid?>> ResolveMediaAsync(ChangeOperation operation, CancellationToken cancellationToken)
    {
        if (!await context.MediaAssets.AnyAsync(asset => asset.Id == operation.ItemId && asset.SiteId == SiteId, cancellationToken))
        {
            return Result<Guid?>.Failure(ChangeSetErrorCodes.ItemNotFound, $"There is no media asset {operation.ItemId} in this site.");
        }

        var allowed = await permissions.CheckMediaAsync(AgentOperationOf(operation.Kind), cancellationToken);
        if (!allowed.IsSuccess)
        {
            return Result<Guid?>.Failure(allowed.Errors);
        }

        var wanted = new[] { operation.BaseVersion, operation.ProposedVersion }.OfType<int>().ToList();
        var stored = await context.MediaAssetVersions
            .AsNoTracking()
            .Where(version => version.AssetId == operation.ItemId && wanted.Contains(version.Version))
            .Select(version => new { version.Version, version.Id })
            .ToListAsync(cancellationToken);

        var missing = wanted.Except(stored.Select(version => version.Version)).ToList();
        if (missing.Count > 0)
        {
            return Result<Guid?>.Failure(missing.Select(version => new Error(
                ChangeSetErrorCodes.VersionNotFound,
                $"Media asset {operation.ItemId} has no version {version}.")));
        }

        return Result<Guid?>.Success(stored.SingleOrDefault(version => version.Version == operation.BaseVersion)?.Id);
    }

    // The diff of every media operation, by its position in the set, from all the versions they name in one query.
    private async Task<Dictionary<int, ItemDiff>> MediaDiffsAsync(ChangeSet set, CancellationToken cancellationToken)
    {
        var operations = set.Operations
            .Select((operation, position) => (Operation: operation, Position: position))
            .Where(item => item.Operation.ItemKind == ItemKind.Media)
            .ToList();
        if (operations.Count == 0)
        {
            return [];
        }

        var ids = operations.Select(item => item.Operation.ItemId).Distinct().ToList();
        var numbers = operations.SelectMany(item => new[] { item.Operation.BaseVersion, item.Operation.ProposedVersion }).OfType<int>().Distinct().ToList();
        var rows = await context.MediaAssetVersions
            .AsNoTracking()
            .Where(version => ids.Contains(version.AssetId) && numbers.Contains(version.Version))
            .ToListAsync(cancellationToken);
        var versions = rows.ToDictionary(row => (row.AssetId, row.Version), row => row.ToVersion());

        // Versions are never changed and an operation is checked against them when it is added, so a missing one is a broken store.
        MediaAssetVersion? Stored(Guid assetId, int? number)
        {
            return number is { } version
                ? versions.GetValueOrDefault((assetId, version))
                    ?? throw new InvalidOperationException($"Change set {set.Id} points at version {version} of media asset {assetId}, which is not stored.")
                : null;
        }

        return operations.ToDictionary(
            item => item.Position,
            item => ItemDiff.ForMedia(item.Operation, Stored(item.Operation.ItemId, item.Operation.BaseVersion), Stored(item.Operation.ItemId, item.Operation.ProposedVersion)));
    }

    // Each asset the operations name with its published version and the rows of their base versions.
    private async Task<Dictionary<Guid, PublishedEntry>> PublishedMediaAsync(IReadOnlyList<ChangeOperation> operations, CancellationToken cancellationToken)
    {
        var mediaOperations = operations.Where(operation => operation.ItemKind == ItemKind.Media).ToList();
        if (mediaOperations.Count == 0)
        {
            return [];
        }

        var ids = mediaOperations.Select(operation => operation.ItemId).Distinct().ToList();
        var bases = mediaOperations.Select(operation => operation.BaseVersion).OfType<int>().Distinct().ToList();

        var assets = await context.MediaAssets
            .AsNoTracking()
            .Where(asset => asset.SiteId == SiteId && ids.Contains(asset.Id))
            .Select(asset => new { asset.Id, asset.PublishedVersion })
            .ToListAsync(cancellationToken);
        var versionIds = (await context.MediaAssetVersions
            .AsNoTracking()
            .Where(version => ids.Contains(version.AssetId) && bases.Contains(version.Version))
            .Select(version => new { version.AssetId, version.Version, version.Id })
            .ToListAsync(cancellationToken))
            .ToLookup(version => version.AssetId);

        return assets.ToDictionary(
            asset => asset.Id,
            asset => new PublishedEntry(asset.PublishedVersion, Deleted: false, versionIds[asset.Id].ToDictionary(version => version.Version, version => version.Id)));
    }

    private static string Capitalize(string text)
    {
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static ContentTypeDefinition ReadDefinition(string subject, string json)
    {
        var definition = ContentTypeDefinition.Parse(json);

        return definition.IsSuccess
            ? definition.Value
            : throw new InvalidOperationException(
                $"The definition {subject} is stored in a form that cannot be read: " + string.Join("; ", definition.Errors.Select(error => error.Message)));
    }

    private Task<Guid?> OpenSetOfSessionAsync(Initiator initiator, string sessionId, CancellationToken cancellationToken)
    {
        var (kind, initiatorId) = InitiatorKinds.Describe(initiator);

        return context.ChangeSets
            .AsNoTracking()
            .Where(row => row.SiteId == SiteId
                && row.InitiatorKind == kind
                && row.InitiatorId == initiatorId
                && row.SessionId == sessionId
                && row.State == ChangeSetState.Open)
            .Select(row => (Guid?)row.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<StoredChangeSet?> ReadAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await context.ChangeSets
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == id && candidate.SiteId == SiteId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var operations = await context.ChangeSetOperations
            .AsNoTracking()
            .Where(operation => operation.ChangeSetId == id)
            .OrderBy(operation => operation.Position)
            .ToListAsync(cancellationToken);

        var set = ChangeSet.Restore(
            row.Id,
            row.SiteId,
            row.Rationale,
            row.Sources,
            ToInitiator(row),
            row.SessionId,
            operations.Select(operation => ChangeOperation.Restore(
                operation.Kind,
                operation.ItemKind,
                operation.ItemId,
                operation.BaseVersion,
                operation.ProposedVersion)),
            row.State,
            row.RejectionReason,
            row.RevertsChangeSetId);

        return new StoredChangeSet(set, row.Revision);
    }

    private Initiator Initiator()
    {
        return currentInitiator.Current
            ?? throw new InvalidOperationException("No initiator is known for this operation; a change set records who started it (CHG-01).");
    }

    private static async Task<Result<ChangeSet>> CommitAsync(
        IUnitOfWorkScope scope,
        ChangeSet set,
        Result<ChangeSet> conflict,
        CancellationToken cancellationToken)
    {
        try
        {
            await scope.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is DbUpdateException update && DatabaseErrors.IsUniqueViolation(update)
            || DatabaseErrors.IsConcurrencyFailure(exception))
        {
            // The scope rolls back when the caller disposes it.
            return conflict;
        }

        return Result<ChangeSet>.Success(set);
    }

    private static Initiator ToInitiator(ChangeSetRow row)
    {
        return row.InitiatorKind switch
        {
            InitiatorKinds.User => new UserInitiator(row.InitiatorId, row.InitiatorName),
            InitiatorKinds.Agent => new AgentInitiator(row.InitiatorId, row.InitiatorName),
            _ => throw new InvalidOperationException($"Change set {row.Id} names an unknown initiator kind '{row.InitiatorKind}'."),
        };
    }

    // The same subject type the item's own events use, so an item's history shows the change sets that touched it.
    private static string ItemSubjectType(ItemKind kind)
    {
        return kind == ItemKind.Schema ? "content_type" : kind.ToString().ToLowerInvariant();
    }

    private static AgentOperation AgentOperationOf(OperationKind kind)
    {
        return kind switch
        {
            OperationKind.Create => AgentOperation.Create,
            OperationKind.Update => AgentOperation.Update,
            OperationKind.Delete => AgentOperation.Delete,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown operation kind."),
        };
    }

    private static string TransitionOperation(ChangeSetState state)
    {
        return state switch
        {
            ChangeSetState.Open => "change_set.reopened",
            ChangeSetState.ChecksRunning => "change_set.submitted",
            ChangeSetState.ChecksFailed => "change_set.checks_failed",
            ChangeSetState.AwaitingReview => "change_set.awaiting_review",
            ChangeSetState.Approved => "change_set.approved",
            ChangeSetState.Published => "change_set.published",
            ChangeSetState.Rejected => "change_set.rejected",
            ChangeSetState.Reverted => "change_set.reverted",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown change set state."),
        };
    }

    private static Result<ChangeSet> NotFound(Guid id)
    {
        return Result<ChangeSet>.Failure(ChangeSetErrorCodes.ChangeSetNotFound, $"There is no change set {id} in this site.");
    }

    private static Result<ChangeSet> Conflict(Guid id)
    {
        return Result<ChangeSet>.Failure(
            ChangeSetErrorCodes.Conflict,
            $"Change set {id} was changed by another call at the same time; read it again and retry.");
    }

    private sealed record StoredChangeSet(ChangeSet ChangeSet, int Revision);

    // Revision is null for a set the write opens. Position is the operation's place in the set, and Extends says whether the write
    // extends an operation the set already holds.
    internal sealed record PendingOperation(ChangeSet ChangeSet, int? Revision, int Position, bool Extends);

    // Name says which item it is in a message, such as "the content type 'product'".
    private sealed record PublishableItem(ChangeOperation Operation, string Name, Guid? PreviousVersionId);

    private sealed record PublishedEntry(int? PublishedVersion, bool Deleted, Dictionary<int, Guid> VersionIds);
}
