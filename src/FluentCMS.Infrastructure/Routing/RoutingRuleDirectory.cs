using FluentCMS.Core.Audit;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Core.Routing;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.Routing;

// Expected failures are found before the unit of work begins, so they leave a caller's outer unit of work able to commit. Reads
// never track rows, so a long-lived context always sees what is stored.
internal sealed class RoutingRuleDirectory(
    FluentCmsDbContext context,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    ICurrentInitiator currentInitiator) : IRoutingRuleDirectory
{
    private const string SubjectType = "routing_rule";

    // Until multi-site (MST-01), every rule belongs to the one site.
    private static readonly Guid SiteId = Site.Default.Id;

    public async Task<IReadOnlyList<RoutingRuleSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await context.RoutingRules
            .AsNoTracking()
            .Where(rule => rule.SiteId == SiteId)
            .OrderBy(rule => rule.Name)
            .Select(rule => new RoutingRuleSummary(rule.Id, rule.Name, rule.Version, rule.ActiveVersion))
            .ToListAsync(cancellationToken);
    }

    public Task<Result<RoutingRule>> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        return ReadAsync(name, version: null, cancellationToken);
    }

    public Task<Result<RoutingRule>> GetVersionAsync(string name, int version, CancellationToken cancellationToken = default)
    {
        return ReadAsync(name, version, cancellationToken);
    }

    public async Task<IReadOnlyList<RoutingRule>> ListActiveAsync(CancellationToken cancellationToken = default)
    {
        var rows = await (
                from rule in context.RoutingRules.AsNoTracking()
                join version in context.RoutingRuleVersions.AsNoTracking() on rule.Id equals version.RoutingRuleId
                where rule.SiteId == SiteId && rule.ActiveVersion == version.Version
                orderby rule.Name
                select new { Rule = rule, Version = version })
            .ToListAsync(cancellationToken);

        return rows.Select(row => ToRule(row.Rule, row.Version)).ToList();
    }

    public async Task<Result<RoutingRule>> CreateAsync(NewRoutingRule rule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (RefuseAllButPeople("create") is { } refused)
        {
            return refused;
        }

        var created = RoutingRule.Create(SiteId, rule.Name, rule.Definition);
        if (!created.IsSuccess)
        {
            return created;
        }

        var stored = created.Value;
        if (await context.RoutingRules.AnyAsync(row => row.SiteId == SiteId && row.Name == stored.Name, cancellationToken))
        {
            return Result<RoutingRule>.Failure([NameTaken(stored.Name)]);
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        context.RoutingRules.Add(new RoutingRuleRow { Id = stored.Id, SiteId = stored.SiteId, Name = stored.Name, Version = stored.Version });
        context.RoutingRuleVersions.Add(VersionRow(stored));
        await auditLog.AppendAsync(new NewAuditEvent("routing_rule.created") { SubjectType = SubjectType, SubjectId = stored.Id }, cancellationToken);

        return await CommitAsync(scope, stored, NameTaken(stored.Name), cancellationToken);
    }

    public async Task<Result<RoutingRule>> ReviseAsync(
        string name,
        int baseVersion,
        RoutingRuleDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(definition);

        if (RefuseAllButPeople("revise") is { } refused)
        {
            return refused;
        }

        var row = await FindAsync(name, cancellationToken);
        if (row is null)
        {
            return NotFound(name);
        }

        if (row.Version != baseVersion)
        {
            return Result<RoutingRule>.Failure([VersionConflict(name, baseVersion)]);
        }

        var current = await VersionRowAsync(row.Id, row.Version, cancellationToken);
        var revised = ToRule(row, current!).Revise(definition);
        if (!revised.IsSuccess)
        {
            return revised;
        }

        var rule = revised.Value;
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        // Moves the pointer only from the version the caller read; a concurrent revision that committed first leaves nothing to move.
        var moved = await context.RoutingRules
            .Where(stored => stored.Id == row.Id && stored.Version == baseVersion)
            .ExecuteUpdateAsync(setters => setters.SetProperty(stored => stored.Version, rule.Version), cancellationToken);
        if (moved == 0)
        {
            return Result<RoutingRule>.Failure([VersionConflict(name, baseVersion)]);
        }

        context.RoutingRuleVersions.Add(VersionRow(rule));
        await auditLog.AppendAsync(
            new NewAuditEvent("routing_rule.revised") { SubjectType = SubjectType, SubjectId = rule.Id, PreviousVersionId = current!.Id },
            cancellationToken);

        return await CommitAsync(scope, rule, VersionConflict(name, baseVersion), cancellationToken);
    }

    public async Task<Result<RoutingRule>> ActivateAsync(string name, int version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (RefuseAllButPeople("activate") is { } refused)
        {
            return refused;
        }

        var row = await FindAsync(name, cancellationToken);
        if (row is null)
        {
            return NotFound(name);
        }

        var stored = await VersionRowAsync(row.Id, version, cancellationToken);
        if (stored is null)
        {
            return VersionNotFound(row, version);
        }

        if (row.ActiveVersion == version)
        {
            return Result<RoutingRule>.Failure(RoutingErrorCodes.AlreadyActive, $"Version {version} of the routing rule '{name}' is already the active one.");
        }

        // The rules for definitions may have changed since the version was written, so it is checked again before it routes sets.
        var rule = ToRule(row, stored);
        var valid = rule.Definition.Validate();
        if (!valid.IsSuccess)
        {
            return Result<RoutingRule>.Failure(valid.Errors);
        }

        var previous = row.ActiveVersion is { } active ? await VersionRowAsync(row.Id, active, cancellationToken) : null;
        return await MoveActiveVersionAsync(row, version, "routing_rule.activated", previous, rule.WithActiveVersion(version), cancellationToken);
    }

    public async Task<Result<RoutingRule>> DeactivateAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (RefuseAllButPeople("deactivate") is { } refused)
        {
            return refused;
        }

        var row = await FindAsync(name, cancellationToken);
        if (row is null)
        {
            return NotFound(name);
        }

        if (row.ActiveVersion is not { } active)
        {
            return Result<RoutingRule>.Failure(RoutingErrorCodes.NotActive, $"The routing rule '{name}' is not active.");
        }

        var previous = await VersionRowAsync(row.Id, active, cancellationToken);
        var latest = await VersionRowAsync(row.Id, row.Version, cancellationToken);
        return await MoveActiveVersionAsync(
            row,
            null,
            "routing_rule.deactivated",
            previous,
            ToRule(row, latest!).WithActiveVersion(null),
            cancellationToken);
    }

    private async Task<Result<RoutingRule>> MoveActiveVersionAsync(
        RoutingRuleRow row,
        int? target,
        string operation,
        RoutingRuleVersionRow? previous,
        RoutingRule rule,
        CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        // Moves the pointer only from where it was read, so of two concurrent moves the second changes nothing.
        var read = row.ActiveVersion;
        var moved = await context.RoutingRules
            .Where(stored => stored.Id == row.Id && stored.ActiveVersion == read)
            .ExecuteUpdateAsync(setters => setters.SetProperty(stored => stored.ActiveVersion, target), cancellationToken);
        if (moved == 0)
        {
            return Result<RoutingRule>.Failure(
                RoutingErrorCodes.VersionConflict,
                $"The active version of the routing rule '{row.Name}' was changed by another call at the same time; read it again.");
        }

        await auditLog.AppendAsync(
            new NewAuditEvent(operation) { SubjectType = SubjectType, SubjectId = row.Id, PreviousVersionId = previous?.Id },
            cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return Result<RoutingRule>.Success(rule);
    }

    // Routing rules decide what publishes without a person, so only a person writes them (SEC-02): no agent, whatever its scope, and
    // none of the product's engines (ADR-0012).
    private Result<RoutingRule>? RefuseAllButPeople(string action)
    {
        return currentInitiator.Current switch
        {
            AgentInitiator => Result<RoutingRule>.Failure(RoutingErrorCodes.PeopleOnly, $"Only a person can {action} a routing rule; an agent cannot."),
            SystemInitiator system => Result<RoutingRule>.Failure(
                RoutingErrorCodes.PeopleOnly,
                $"Only a person can {action} a routing rule; the '{system.Component}' engine cannot."),
            _ => null,
        };
    }

    private async Task<Result<RoutingRule>> ReadAsync(string name, int? version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);

        var row = await FindAsync(name, cancellationToken);
        if (row is null)
        {
            return NotFound(name);
        }

        var number = version ?? row.Version;
        var stored = await VersionRowAsync(row.Id, number, cancellationToken);
        return stored is null ? VersionNotFound(row, number) : Result<RoutingRule>.Success(ToRule(row, stored));
    }

    private Task<RoutingRuleRow?> FindAsync(string name, CancellationToken cancellationToken)
    {
        return context.RoutingRules.AsNoTracking().SingleOrDefaultAsync(row => row.SiteId == SiteId && row.Name == name, cancellationToken);
    }

    private Task<RoutingRuleVersionRow?> VersionRowAsync(Guid ruleId, int version, CancellationToken cancellationToken)
    {
        return context.RoutingRuleVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.RoutingRuleId == ruleId && row.Version == version, cancellationToken);
    }

    private static async Task<Result<RoutingRule>> CommitAsync(IUnitOfWorkScope scope, RoutingRule rule, Error conflict, CancellationToken cancellationToken)
    {
        try
        {
            await scope.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (DatabaseErrors.IsUniqueViolation(exception))
        {
            // The scope rolls back when the caller disposes it.
            return Result<RoutingRule>.Failure([conflict]);
        }

        return Result<RoutingRule>.Success(rule);
    }

    private static RoutingRuleVersionRow VersionRow(RoutingRule rule)
    {
        return new RoutingRuleVersionRow
        {
            Id = Guid.CreateVersion7(),
            RoutingRuleId = rule.Id,
            Version = rule.Version,
            Definition = rule.Definition.ToJson(),
        };
    }

    private static RoutingRule ToRule(RoutingRuleRow row, RoutingRuleVersionRow version)
    {
        var definition = RoutingRuleDefinition.Parse(version.Definition);
        if (!definition.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Version {version.Version} of the routing rule '{row.Name}' is stored in a form that cannot be read: "
                + string.Join("; ", definition.Errors.Select(error => error.Message)));
        }

        return RoutingRule.Restore(row.Id, row.SiteId, row.Name, version.Version, row.Version, row.ActiveVersion, definition.Value);
    }

    private static Result<RoutingRule> NotFound(string name)
    {
        return Result<RoutingRule>.Failure(RoutingErrorCodes.RuleNotFound, $"There is no routing rule '{name}' in this site.");
    }

    private static Result<RoutingRule> VersionNotFound(RoutingRuleRow row, int version)
    {
        return Result<RoutingRule>.Failure(
            RoutingErrorCodes.VersionNotFound,
            $"The routing rule '{row.Name}' has no version {version}; its latest version is {row.Version}.");
    }

    private static Error NameTaken(string name)
    {
        return new Error(RoutingErrorCodes.NameTaken, $"There is already a routing rule named '{name}' in this site; revise it or choose another name.");
    }

    private static Error VersionConflict(string name, int baseVersion)
    {
        return new Error(
            RoutingErrorCodes.VersionConflict,
            $"The routing rule '{name}' is no longer at version {baseVersion}; read its latest version and revise that one.");
    }
}
