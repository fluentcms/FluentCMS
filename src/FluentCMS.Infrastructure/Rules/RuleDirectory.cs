using FluentCMS.Core.Audit;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Core.Rules;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.ChangeSets;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.Rules;

// Each write joins its change set as content types do (SCH-05): every expected failure is found before the unit of work, and the
// rule's rows, its event and its operation commit together.
internal sealed class RuleDirectory(
    FluentCmsDbContext context,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    ICurrentInitiator currentInitiator,
    ChangeSetDirectory changeSets) : IRuleDirectory
{
    private const string SubjectType = "rule";

    // Until multi-site (MST-01), every rule belongs to the one site.
    private static readonly Guid SiteId = Site.Default.Id;

    public async Task<IReadOnlyList<Rule>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await (
            from rule in context.Rules.AsNoTracking()
            join version in context.RuleVersions.AsNoTracking() on new { RuleId = rule.Id, rule.Version } equals new { version.RuleId, version.Version }
            where rule.SiteId == SiteId
            select new { Rule = rule, version.Definition })
            .ToListAsync(cancellationToken);

        return [.. rows.OrderBy(row => row.Rule.Slug, StringComparer.Ordinal).Select(row => ToRule(row.Rule, row.Rule.Version, row.Definition))];
    }

    public async Task<Result<Rule>> GetAsync(string slug, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slug);

        var row = await FindAsync(slug, cancellationToken);
        return row is null ? NotFound(slug) : await AtVersionAsync(row, row.Version, cancellationToken);
    }

    public async Task<Result<Rule>> GetActiveAsync(string slug, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slug);

        var row = await FindAsync(slug, cancellationToken);
        if (row is null)
        {
            return NotFound(slug);
        }

        return row.ActiveVersion is { } active
            ? await AtVersionAsync(row, active, cancellationToken)
            : Result<Rule>.Failure(RuleErrorCodes.NotActive, $"The rule '{slug}' is {Describe(row.State)}, so no version of it is active.");
    }

    public async Task<Result<Rule>> GetVersionAsync(string slug, int version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slug);

        var row = await FindAsync(slug, cancellationToken);
        return row is null ? NotFound(slug) : await AtVersionAsync(row, version, cancellationToken);
    }

    public async Task<Result<Rule>> CreateAsync(NewRule rule, ChangeSetTarget changeSet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(changeSet);

        if (PeopleOnly("write") is { } refused)
        {
            return refused;
        }

        var created = Rule.Create(SiteId, rule);
        if (!created.IsSuccess)
        {
            return created;
        }

        var written = created.Value;
        if (await context.Rules.AnyAsync(row => row.SiteId == SiteId && row.Slug == written.Slug, cancellationToken))
        {
            return Result<Rule>.Failure([SlugTaken(written.Slug)]);
        }

        var pending = await changeSets.PrepareAsync(changeSet, Operation(OperationKind.Create, written.Id, null, written.Version), itemPublished: false, cancellationToken);
        if (!pending.IsSuccess)
        {
            return Result<Rule>.Failure(pending.Errors);
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var begun = await changeSets.BeginWriteAsync(pending.Value, cancellationToken);
        if (!begun.IsSuccess)
        {
            return Result<Rule>.Failure(begun.Errors);
        }

        context.Rules.Add(new RuleRow { Id = written.Id, SiteId = written.SiteId, Slug = written.Slug, Version = written.Version, State = RuleState.Draft });
        context.RuleVersions.Add(VersionRow(written));
        await auditLog.AppendAsync(
            new NewAuditEvent("rule.created") { SubjectType = SubjectType, SubjectId = written.Id, ChangeSetId = pending.Value.ChangeSet.Id },
            cancellationToken);
        await changeSets.RecordAsync(pending.Value, null, cancellationToken);

        return await CommitAsync(scope, written, CommitConflict(changeSet, pending.Value, SlugTaken(written.Slug)), cancellationToken);
    }

    public async Task<Result<Rule>> ReviseAsync(
        string slug,
        int baseVersion,
        RuleDefinition definition,
        ChangeSetTarget changeSet,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slug);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(changeSet);

        if (PeopleOnly("write") is { } refused)
        {
            return refused;
        }

        var row = await FindAsync(slug, cancellationToken);
        if (row is null)
        {
            return NotFound(slug);
        }

        // A set that already holds the rule is revised from the version it proposes; otherwise from the latest or the active version.
        var own = await changeSets.ProposedVersionAsync(changeSet, ItemKind.Rule, row.Id, cancellationToken);
        if (own is { } proposed ? baseVersion != proposed : baseVersion != row.Version && baseVersion != row.ActiveVersion)
        {
            return Result<Rule>.Failure(
                RuleErrorCodes.VersionConflict,
                $"The rule '{slug}' is at version {row.Version}{(row.ActiveVersion is { } active ? $" with version {active} active" : string.Empty)}"
                + $"{(own is { } held ? $", and this change set proposes version {held}" : string.Empty)}, so it cannot be revised from version {baseVersion}; "
                + "read it again and revise from what you read.");
        }

        // A draft is only worth building on while a set that can still publish proposes it.
        if (row.ActiveVersion is { } live
            && baseVersion != live
            && !await changeSets.ProposesAsync(ItemKind.Rule, row.Id, baseVersion, cancellationToken))
        {
            return Result<Rule>.Failure(
                RuleErrorCodes.VersionConflict,
                $"Version {baseVersion} of the rule '{slug}' is a draft that no open change set proposes any more, so it will never be active; "
                + $"revise the active version {live} instead.");
        }

        var errors = definition.Validate();
        if (errors.Count > 0)
        {
            return Result<Rule>.Failure(errors);
        }

        // Numbered after the latest version, whichever version the revision changes.
        var based = await context.RuleVersions.AsNoTracking().SingleAsync(version => version.RuleId == row.Id && version.Version == baseVersion, cancellationToken);
        var revised = Rule.Restore(row.Id, row.SiteId, row.Slug, row.Version + 1, definition, row.ActiveVersion, row.State);

        var pending = await changeSets.PrepareAsync(
            changeSet,
            Operation(OperationKind.Update, row.Id, baseVersion, revised.Version),
            itemPublished: row.ActiveVersion is not null,
            cancellationToken);
        if (!pending.IsSuccess)
        {
            return Result<Rule>.Failure(pending.Errors);
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var begun = await changeSets.BeginWriteAsync(pending.Value, cancellationToken);
        if (!begun.IsSuccess)
        {
            return Result<Rule>.Failure(begun.Errors);
        }

        // Moves the latest version only from the one this call read; a concurrent revision that committed first leaves nothing to move.
        var moved = await context.Rules
            .Where(stored => stored.Id == row.Id && stored.Version == row.Version)
            .ExecuteUpdateAsync(setters => setters.SetProperty(stored => stored.Version, revised.Version), cancellationToken);
        if (moved == 0)
        {
            return Result<Rule>.Failure(RuleErrorCodes.VersionConflict, $"The rule '{slug}' was revised at the same time; read it again and revise from what you read.");
        }

        context.RuleVersions.Add(VersionRow(revised));
        await auditLog.AppendAsync(
            new NewAuditEvent("rule.revised") { SubjectType = SubjectType, SubjectId = row.Id, ChangeSetId = pending.Value.ChangeSet.Id, PreviousVersionId = based.Id },
            cancellationToken);
        await changeSets.RecordAsync(pending.Value, based.Id, cancellationToken);

        return await CommitAsync(scope, revised, CommitConflict(changeSet, pending.Value, Conflict(slug)), cancellationToken);
    }

    public async Task<Result<Rule>> RetireAsync(string slug, ChangeSetTarget changeSet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slug);
        ArgumentNullException.ThrowIfNull(changeSet);

        if (PeopleOnly("retire") is { } refused)
        {
            return refused;
        }

        var row = await FindAsync(slug, cancellationToken);
        if (row is null)
        {
            return NotFound(slug);
        }

        if (row.ActiveVersion is not { } active)
        {
            return Result<Rule>.Failure(RuleErrorCodes.NotActive, $"The rule '{slug}' is {Describe(row.State)}, so there is no active version to retire.");
        }

        var activeRow = await context.RuleVersions.AsNoTracking().SingleAsync(version => version.RuleId == row.Id && version.Version == active, cancellationToken);
        var pending = await changeSets.PrepareAsync(changeSet, Operation(OperationKind.Delete, row.Id, active, null), itemPublished: true, cancellationToken);
        if (!pending.IsSuccess)
        {
            return Result<Rule>.Failure(pending.Errors);
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var begun = await changeSets.BeginWriteAsync(pending.Value, cancellationToken);
        if (!begun.IsSuccess)
        {
            return Result<Rule>.Failure(begun.Errors);
        }

        await changeSets.RecordAsync(pending.Value, activeRow.Id, cancellationToken);
        return await CommitAsync(scope, ToRule(row, active, activeRow.Definition), CommitConflict(changeSet, pending.Value, Conflict(slug)), cancellationToken);
    }

    private Result<Rule>? PeopleOnly(string action)
    {
        return currentInitiator.Current switch
        {
            AgentInitiator => Result<Rule>.Failure(RuleErrorCodes.PeopleOnly, $"Only a person can {action} a rule; an agent reads the rule book and cannot."),
            SystemInitiator system => Result<Rule>.Failure(RuleErrorCodes.PeopleOnly, $"The '{system.Component}' engine cannot {action} a rule; a person does."),
            _ => null,
        };
    }

    private Task<RuleRow?> FindAsync(string slug, CancellationToken cancellationToken)
    {
        return context.Rules.AsNoTracking().SingleOrDefaultAsync(row => row.SiteId == SiteId && row.Slug == slug, cancellationToken);
    }

    private async Task<Result<Rule>> AtVersionAsync(RuleRow row, int version, CancellationToken cancellationToken)
    {
        var stored = await context.RuleVersions.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.RuleId == row.Id && candidate.Version == version, cancellationToken);
        return stored is null
            ? Result<Rule>.Failure(RuleErrorCodes.VersionNotFound, $"The rule '{row.Slug}' has no version {version}; its latest version is {row.Version}.")
            : Result<Rule>.Success(ToRule(row, version, stored.Definition));
    }

    // Rebuilds a stored version as the rule at that version, with the rule's own state and active version.
    private static Rule ToRule(RuleRow row, int version, string definition)
    {
        var parsed = RuleDefinition.Parse(definition);
        return parsed.IsSuccess
            ? Rule.Restore(row.Id, row.SiteId, row.Slug, version, parsed.Value, row.ActiveVersion, row.State)
            : throw new InvalidOperationException($"Version {version} of the rule '{row.Slug}' cannot be read: {parsed.Errors[0].Message}");
    }

    private static RuleVersionRow VersionRow(Rule rule)
    {
        return new RuleVersionRow { Id = Guid.CreateVersion7(), RuleId = rule.Id, Version = rule.Version, Definition = rule.Definition.ToJson() };
    }

    private static ChangeOperation Operation(OperationKind kind, Guid ruleId, int? baseVersion, int? proposedVersion)
    {
        return ChangeOperation.Create(kind, ItemKind.Rule, ruleId, baseVersion, proposedVersion).Value;
    }

    private static async Task<Result<Rule>> CommitAsync(IUnitOfWorkScope scope, Rule rule, Error conflict, CancellationToken cancellationToken)
    {
        try
        {
            await scope.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (DatabaseErrors.IsUniqueViolation(exception))
        {
            // The scope rolls back when the caller disposes it.
            return Result<Rule>.Failure([conflict]);
        }

        return Result<Rule>.Success(rule);
    }

    // A unique index at commit is this write's own conflict or, when the write opened its session's set, possibly a concurrent first
    // write of the same session.
    private static Error CommitConflict(ChangeSetTarget target, ChangeSetDirectory.PendingOperation pending, Error own)
    {
        return pending.Revision is null && target.SessionId is { } session
            ? new Error(ChangeSetErrorCodes.Conflict, $"Another call wrote this rule or opened a change set for session '{session}' at the same time; read it again and retry.")
            : own;
    }

    private static Result<Rule> NotFound(string slug)
    {
        return Result<Rule>.Failure(RuleErrorCodes.NotFound, $"There is no rule '{slug}'; list the rule book for the rules it holds.");
    }

    private static Error SlugTaken(string slug)
    {
        return new Error(RuleErrorCodes.SlugTaken, $"A rule named '{slug}' already exists; revise it, or choose another slug.");
    }

    private static Error Conflict(string slug)
    {
        return new Error(RuleErrorCodes.VersionConflict, $"The rule '{slug}' changed at the same time; read it again and retry.");
    }

    private static string Describe(RuleState state)
    {
        return state.ToString().ToLowerInvariant();
    }
}
