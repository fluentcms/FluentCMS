using FluentCMS.Core.Agents;
using FluentCMS.Core.Audit;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.ChangeSets;

// Expected failures are found before the unit of work begins, so they leave a caller's outer unit of work able to commit.
// Reads never track rows, and updates go straight to the database, so a long-lived context always sees what is stored.
internal sealed class ChangeSetDirectory(
    FluentCmsDbContext context,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    ICurrentInitiator currentInitiator,
    IAgentPermissions permissions) : IChangeSetDirectory
{
    // AgentActivity counts these events for the change-sets-per-day cap.
    internal const string OpenedOperation = "change_set.opened";

    private const string SubjectType = "change_set";

    // Until multi-site (MST-01), every set belongs to the one site (SCH-07).
    private static readonly Guid SiteId = Site.Default.Id;

    public async Task<Result<ChangeSet>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var stored = await ReadAsync(id, cancellationToken);

        return stored is null ? NotFound(id) : Result<ChangeSet>.Success(stored.ChangeSet);
    }

    public async Task<Result<ChangeSetDiff>> DiffAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var stored = await ReadAsync(id, cancellationToken);
        if (stored is null)
        {
            return Result<ChangeSetDiff>.Failure(NotFound(id).Errors);
        }

        var set = stored.ChangeSet;
        var definitions = await DefinitionsAsync(set.Operations, cancellationToken);

        var items = set.Operations
            .Select(operation => operation.ItemKind == ItemKind.Schema
                ? ItemDiff.ForSchema(
                    operation,
                    Definition(definitions, set.Id, operation.ItemId, operation.BaseVersion),
                    Definition(definitions, set.Id, operation.ItemId, operation.ProposedVersion))
                : throw new InvalidOperationException(
                    $"Change set {set.Id} holds an operation on a {ItemSubjectType(operation.ItemKind)} item, which no diff covers yet."))
            .ToList();

        return Result<ChangeSetDiff>.Success(new ChangeSetDiff(set.Id, items));
    }

    public async Task<Result<ChangeSet>> OpenAsync(NewChangeSet changeSet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changeSet);

        var opened = ChangeSet.Open(SiteId, changeSet.Rationale, changeSet.Sources, Initiator());
        if (!opened.IsSuccess)
        {
            return opened;
        }

        var allowed = await permissions.CheckNewChangeSetAsync(cancellationToken);
        if (!allowed.IsSuccess)
        {
            return Result<ChangeSet>.Failure(allowed.Errors);
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
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

        var allowed = await permissions.CheckNewChangeSetAsync(cancellationToken);
        if (!allowed.IsSuccess)
        {
            return Result<ChangeSet>.Failure(allowed.Errors);
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        await InsertAsync(set, cancellationToken);
        await InsertOperationAsync(set, previousVersion.Value, cancellationToken);

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
        return MoveAsync(changeSetId, target, set => set.TransitionTo(target), cancellationToken);
    }

    public Task<Result<ChangeSet>> RejectAsync(Guid changeSetId, string reason, CancellationToken cancellationToken = default)
    {
        return MoveAsync(changeSetId, ChangeSetState.Rejected, set => set.Reject(reason), cancellationToken);
    }

    private async Task<Result<ChangeSet>> AddAsync(
        StoredChangeSet stored,
        ChangeOperation operation,
        IReadOnlyList<string> sources,
        CancellationToken cancellationToken)
    {
        var set = stored.ChangeSet;

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
        var allowed = await permissions.CheckChangeSetSizeAsync(set, cancellationToken);
        if (!allowed.IsSuccess)
        {
            return Result<ChangeSet>.Failure(allowed.Errors);
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        if (!await UpdateAsync(stored.Revision, set, cancellationToken))
        {
            return Conflict(set.Id);
        }

        await InsertOperationAsync(set, previousVersion.Value, cancellationToken);

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

        await auditLog.AppendAsync(
            new NewAuditEvent(TransitionOperation(set.State)) { SubjectType = SubjectType, SubjectId = set.Id, ChangeSetId = set.Id },
            cancellationToken);

        return await CommitAsync(scope, set, Conflict(set.Id), cancellationToken);
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
        });

        await auditLog.AppendAsync(
            new NewAuditEvent(OpenedOperation) { SubjectType = SubjectType, SubjectId = set.Id, ChangeSetId = set.Id, Sources = set.Sources },
            cancellationToken);
    }

    // The newest operation of the set, with the stored version it was based on as the event's previous version (AUD-01).
    private async Task InsertOperationAsync(ChangeSet set, Guid? previousVersionId, CancellationToken cancellationToken)
    {
        var operation = set.Operations[^1];

        context.ChangeSetOperations.Add(new ChangeSetOperationRow
        {
            Id = Guid.CreateVersion7(),
            ChangeSetId = set.Id,
            Position = set.Operations.Count - 1,
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
        if (operation.ItemKind != ItemKind.Schema)
        {
            return Result<Guid?>.Failure(
                ChangeSetErrorCodes.ItemKindUnavailable,
                $"There are no stored {ItemSubjectType(operation.ItemKind)} items yet, so a change set cannot point at one; "
                + "only schema operations can be added.");
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
            row.RejectionReason);

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
        catch (DbUpdateException exception) when (DatabaseErrors.IsUniqueViolation(exception))
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
}
