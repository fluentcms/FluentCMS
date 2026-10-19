using FluentCMS.Core.Audit;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.Checks;

// Expected failures are found before the unit of work begins, so they leave a caller's outer unit of work able to commit. Reads
// never track rows, so a long-lived context always sees what is stored. The services are this scope's, which check types read
// as the caller.
internal sealed class CheckDirectory(
    FluentCmsDbContext context,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    ICurrentInitiator currentInitiator,
    CheckTypeRegistry checkTypes,
    IContentTypeDirectory contentTypes,
    IServiceProvider services) : ICheckDirectory
{
    private const string SubjectType = "check";

    // Until multi-site (MST-01), every check belongs to the one site.
    private static readonly Guid SiteId = Site.Default.Id;

    public async Task<IReadOnlyList<CheckSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await context.Checks
            .AsNoTracking()
            .Where(check => check.SiteId == SiteId)
            .OrderBy(check => check.Name)
            .Select(check => new CheckSummary(check.Id, check.Name, check.Version, check.ActiveVersion))
            .ToListAsync(cancellationToken);
    }

    public Task<Result<Check>> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        return ReadAsync(name, version: null, cancellationToken);
    }

    public Task<Result<Check>> GetVersionAsync(string name, int version, CancellationToken cancellationToken = default)
    {
        return ReadAsync(name, version, cancellationToken);
    }

    public async Task<IReadOnlyList<Check>> ListActiveAsync(CancellationToken cancellationToken = default)
    {
        var rows = await (
                from check in context.Checks.AsNoTracking()
                join version in context.CheckVersions.AsNoTracking() on check.Id equals version.CheckId
                where check.SiteId == SiteId && check.ActiveVersion == version.Version
                orderby check.Name
                select new { Check = check, Version = version })
            .ToListAsync(cancellationToken);

        return rows.Select(row => ToCheck(row.Check, row.Version)).ToList();
    }

    public async Task<Result<Check>> CreateAsync(NewCheck check, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(check);

        if (RefuseEngine("create") is { } refused)
        {
            return refused;
        }

        var created = Check.Create(SiteId, check.Name, check.Definition, checkTypes);
        if (!created.IsSuccess)
        {
            return created;
        }

        var stored = created.Value;
        if (await context.Checks.AnyAsync(row => row.SiteId == SiteId && row.Name == stored.Name, cancellationToken))
        {
            return Result<Check>.Failure([NameTaken(stored.Name)]);
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        context.Checks.Add(new CheckRow { Id = stored.Id, SiteId = stored.SiteId, Name = stored.Name, Version = stored.Version });
        context.CheckVersions.Add(VersionRow(stored));
        await auditLog.AppendAsync(new NewAuditEvent("check.created") { SubjectType = SubjectType, SubjectId = stored.Id }, cancellationToken);

        return await CommitAsync(scope, stored, NameTaken(stored.Name), cancellationToken);
    }

    public async Task<Result<Check>> ReviseAsync(string name, int baseVersion, CheckDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(definition);

        if (RefuseEngine("revise") is { } refused)
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
            return Result<Check>.Failure([VersionConflict(name, baseVersion)]);
        }

        var current = await VersionRowAsync(row.Id, row.Version, cancellationToken);
        var revised = ToCheck(row, current!).Revise(definition, checkTypes);
        if (!revised.IsSuccess)
        {
            return revised;
        }

        var check = revised.Value;
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        // Moves the pointer only from the version the caller read; a concurrent revision that committed first leaves nothing to move.
        var moved = await context.Checks
            .Where(stored => stored.Id == row.Id && stored.Version == baseVersion)
            .ExecuteUpdateAsync(setters => setters.SetProperty(stored => stored.Version, check.Version), cancellationToken);
        if (moved == 0)
        {
            return Result<Check>.Failure([VersionConflict(name, baseVersion)]);
        }

        context.CheckVersions.Add(VersionRow(check));
        await auditLog.AppendAsync(
            new NewAuditEvent("check.revised") { SubjectType = SubjectType, SubjectId = check.Id, PreviousVersionId = current!.Id },
            cancellationToken);

        return await CommitAsync(scope, check, VersionConflict(name, baseVersion), cancellationToken);
    }

    public async Task<Result<Check>> ActivateAsync(string name, int version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (RefuseAgent("activate") is { } refused)
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
            return Result<Check>.Failure(CheckErrorCodes.AlreadyActive, $"Version {version} of the check '{name}' is already the active one.");
        }

        var check = ToCheck(row, stored);

        // The installed check types may have changed since the version was written, so it is checked again before its examples run.
        var valid = check.Definition.Validate(checkTypes);
        if (!valid.IsSuccess)
        {
            return Result<Check>.Failure(valid.Errors);
        }

        var definitions = await ExampleContentTypesAsync(check.Definition, cancellationToken);
        if (!definitions.IsSuccess)
        {
            return Result<Check>.Failure(definitions.Errors);
        }

        var examples = await check.Definition.RunExamplesAsync(checkTypes, definitions.Value, services, cancellationToken);
        if (!examples.IsSuccess)
        {
            return Result<Check>.Failure(examples.Errors);
        }

        var previous = row.ActiveVersion is { } active ? await VersionRowAsync(row.Id, active, cancellationToken) : null;
        return await MoveActiveVersionAsync(row, version, "check.activated", previous, check.WithActiveVersion(version), cancellationToken);
    }

    public async Task<Result<Check>> DeactivateAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (RefuseAgent("deactivate") is { } refused)
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
            return Result<Check>.Failure(CheckErrorCodes.NotActive, $"The check '{name}' is not active.");
        }

        var previous = await VersionRowAsync(row.Id, active, cancellationToken);
        var latest = await VersionRowAsync(row.Id, row.Version, cancellationToken);
        return await MoveActiveVersionAsync(row, null, "check.deactivated", previous, ToCheck(row, latest!).WithActiveVersion(null), cancellationToken);
    }

    private async Task<Result<Check>> MoveActiveVersionAsync(
        CheckRow row,
        int? target,
        string operation,
        CheckVersionRow? previous,
        Check check,
        CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        // Moves the pointer only from where it was read, so of two concurrent moves the second changes nothing.
        var read = row.ActiveVersion;
        var moved = await context.Checks
            .Where(stored => stored.Id == row.Id && stored.ActiveVersion == read)
            .ExecuteUpdateAsync(setters => setters.SetProperty(stored => stored.ActiveVersion, target), cancellationToken);
        if (moved == 0)
        {
            return Result<Check>.Failure(
                CheckErrorCodes.VersionConflict,
                $"The active version of the check '{row.Name}' was changed by another call at the same time; read it again.");
        }

        await auditLog.AppendAsync(
            new NewAuditEvent(operation) { SubjectType = SubjectType, SubjectId = row.Id, PreviousVersionId = previous?.Id },
            cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return Result<Check>.Success(check);
    }

    // The current definition of each content type the examples name, so types that read the definition see the one entries use.
    private async Task<Result<IReadOnlyDictionary<string, ContentTypeDefinition>>> ExampleContentTypesAsync(
        CheckDefinition definition,
        CancellationToken cancellationToken)
    {
        var found = new Dictionary<string, ContentTypeDefinition>(StringComparer.Ordinal);
        var read = new HashSet<string>(StringComparer.Ordinal);
        var errors = new List<Error>();

        foreach (var example in definition.Examples.Where(example => example.ContentType is not null))
        {
            var slug = example.ContentType!;
            if (!read.Add(slug))
            {
                continue;
            }

            var type = await contentTypes.GetAsync(slug, cancellationToken);
            if (type.IsSuccess)
            {
                found[slug] = type.Value.Definition;
            }
            else
            {
                errors.Add(new Error(
                    CheckErrorCodes.ExampleInvalid,
                    $"Example '{example.Name}' is of the content type '{slug}', which does not exist in this site."));
            }
        }

        return errors.Count == 0
            ? Result<IReadOnlyDictionary<string, ContentTypeDefinition>>.Success(found)
            : Result<IReadOnlyDictionary<string, ContentTypeDefinition>>.Failure(errors);
    }

    // Activating decides what guards content, which only a person does (D17, SEC-02): no agent, whatever its scope, and no engine.
    // A check says what guards content, which a person or an agent proposes and the product's engines only run (ADR-0012).
    private Result<Check>? RefuseEngine(string action)
    {
        return currentInitiator.Current is SystemInitiator system
            ? Result<Check>.Failure(CheckErrorCodes.PeopleOnly, $"The '{system.Component}' engine cannot {action} a check; a person or an agent does.")
            : null;
    }

    private Result<Check>? RefuseAgent(string action)
    {
        return currentInitiator.Current is AgentInitiator or SystemInitiator
            ? Result<Check>.Failure(CheckErrorCodes.PeopleOnly, $"Only a person can {action} a check; an agent cannot.")
            : null;
    }

    private async Task<Result<Check>> ReadAsync(string name, int? version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);

        var row = await FindAsync(name, cancellationToken);
        if (row is null)
        {
            return NotFound(name);
        }

        var number = version ?? row.Version;
        var stored = await VersionRowAsync(row.Id, number, cancellationToken);
        return stored is null ? VersionNotFound(row, number) : Result<Check>.Success(ToCheck(row, stored));
    }

    private Task<CheckRow?> FindAsync(string name, CancellationToken cancellationToken)
    {
        return context.Checks.AsNoTracking().SingleOrDefaultAsync(row => row.SiteId == SiteId && row.Name == name, cancellationToken);
    }

    private Task<CheckVersionRow?> VersionRowAsync(Guid checkId, int version, CancellationToken cancellationToken)
    {
        return context.CheckVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.CheckId == checkId && row.Version == version, cancellationToken);
    }

    private static async Task<Result<Check>> CommitAsync(IUnitOfWorkScope scope, Check check, Error conflict, CancellationToken cancellationToken)
    {
        try
        {
            await scope.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (DatabaseErrors.IsUniqueViolation(exception))
        {
            // The scope rolls back when the caller disposes it.
            return Result<Check>.Failure([conflict]);
        }

        return Result<Check>.Success(check);
    }

    private static CheckVersionRow VersionRow(Check check)
    {
        return new CheckVersionRow
        {
            Id = Guid.CreateVersion7(),
            CheckId = check.Id,
            Version = check.Version,
            Definition = check.Definition.ToJson(),
        };
    }

    private static Check ToCheck(CheckRow row, CheckVersionRow version)
    {
        var definition = CheckDefinition.Parse(version.Definition);
        if (!definition.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Version {version.Version} of the check '{row.Name}' is stored in a form that cannot be read: "
                + string.Join("; ", definition.Errors.Select(error => error.Message)));
        }

        return Check.Restore(row.Id, row.SiteId, row.Name, version.Version, row.Version, row.ActiveVersion, definition.Value);
    }

    private static Result<Check> NotFound(string name)
    {
        return Result<Check>.Failure(CheckErrorCodes.CheckNotFound, $"There is no check '{name}' in this site.");
    }

    private static Result<Check> VersionNotFound(CheckRow row, int version)
    {
        return Result<Check>.Failure(
            CheckErrorCodes.VersionNotFound,
            $"The check '{row.Name}' has no version {version}; its latest version is {row.Version}.");
    }

    private static Error NameTaken(string name)
    {
        return new Error(CheckErrorCodes.NameTaken, $"There is already a check named '{name}' in this site; revise it or choose another name.");
    }

    private static Error VersionConflict(string name, int baseVersion)
    {
        return new Error(
            CheckErrorCodes.VersionConflict,
            $"The check '{name}' is no longer at version {baseVersion}; read its latest version and revise that one.");
    }
}
