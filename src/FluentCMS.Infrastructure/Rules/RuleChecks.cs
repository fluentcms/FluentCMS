using System.Text.Json;
using FluentCMS.Core.Audit;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Core.Rules;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Checks;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.Rules;

// Every expected failure is found before the unit of work begins. Reads never track rows, so a long-lived context sees what is stored.
internal sealed class RuleChecks(
    FluentCmsDbContext context,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    ICurrentInitiator currentInitiator,
    IJobQueue jobs,
    RuleCompiler compiler,
    CheckDirectory checks) : IRuleChecks
{
    private const string SubjectType = "rule";

    private const string NotCompiled = "No checks have been proposed for this version, so the rule is advisory; propose checks for it.";

    // Until multi-site (MST-01), every rule belongs to the one site.
    private static readonly Guid SiteId = Site.Default.Id;

    public async Task<Result<RuleCompilation>> GetAsync(string slug, int version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slug);

        var rule = await FindAsync(slug, cancellationToken);
        if (rule is null)
        {
            return Result<RuleCompilation>.Failure([NotFound(slug)]);
        }

        if (!await context.RuleVersions.AnyAsync(stored => stored.RuleId == rule.Id && stored.Version == version, cancellationToken))
        {
            return Result<RuleCompilation>.Failure([VersionNotFound(rule, version)]);
        }

        var compilation = await context.RuleCompilations.AsNoTracking()
            .SingleOrDefaultAsync(row => row.RuleId == rule.Id && row.Version == version, cancellationToken);
        var proposals = await context.RuleCheckProposals.AsNoTracking()
            .Where(row => row.RuleId == rule.Id && row.RuleVersion == version)
            .OrderBy(row => row.Number)
            .ToListAsync(cancellationToken);

        return Result<RuleCompilation>.Success(new RuleCompilation(
            rule.Slug,
            version,
            compilation?.Status ?? RuleCompileStatus.Advisory,
            compilation is null ? NotCompiled : compilation.Message,
            compilation?.JobId,
            [.. proposals.Select(ToProposal)]));
    }

    public async Task<Result<RuleCompilation>> CompileAsync(string slug, int version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slug);

        if (PeopleOnly("propose checks for") is { } refused)
        {
            return Result<RuleCompilation>.Failure([refused]);
        }

        var rule = await FindAsync(slug, cancellationToken);
        if (rule is null)
        {
            return Result<RuleCompilation>.Failure([NotFound(slug)]);
        }

        if (!await context.RuleVersions.AnyAsync(stored => stored.RuleId == rule.Id && stored.Version == version, cancellationToken))
        {
            return Result<RuleCompilation>.Failure([VersionNotFound(rule, version)]);
        }

        await using (var scope = await unitOfWork.BeginAsync(cancellationToken))
        {
            await context.RuleCheckProposals
                .Where(row => row.RuleId == rule.Id && row.RuleVersion == version && row.State == RuleProposalState.Proposed)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.State, RuleProposalState.Replaced), cancellationToken);
            await ScheduleAsync(rule.Id, version, replace: true, cancellationToken);
            await auditLog.AppendAsync(new NewAuditEvent("rule.compile_requested") { SubjectType = SubjectType, SubjectId = rule.Id }, cancellationToken);

            try
            {
                await scope.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (DatabaseErrors.IsUniqueViolation(exception))
            {
                return Result<RuleCompilation>.Failure(
                    RuleErrorCodes.VersionConflict,
                    $"Checks for version {version} of the rule '{slug}' were proposed again at the same time; read it again.");
            }
        }

        return await GetAsync(slug, version, cancellationToken);
    }

    public async Task<Result<RuleCheckProposal>> ConfirmAsync(
        string slug,
        int number,
        CheckDefinition? definition = null,
        string? checkName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slug);

        if (PeopleOnly("confirm a check for") is { } refused)
        {
            return Result<RuleCheckProposal>.Failure([refused]);
        }

        var found = await UndecidedAsync(slug, number, cancellationToken);
        if (!found.IsSuccess)
        {
            return Result<RuleCheckProposal>.Failure(found.Errors);
        }

        var (rule, proposal) = found.Value;
        var prepared = await checks.PrepareActiveAsync(new NewCheck(checkName ?? proposal.CheckName, definition ?? ToProposal(proposal).Definition), cancellationToken);
        if (!prepared.IsSuccess)
        {
            return Result<RuleCheckProposal>.Failure(prepared.Errors);
        }

        var check = prepared.Value;
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        await checks.AddActiveAsync(check, cancellationToken);

        // The check is stored first, so the proposal can name it.
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (DatabaseErrors.IsUniqueViolation(exception))
        {
            return Result<RuleCheckProposal>.Failure([CheckDirectory.NameTaken(check.Name)]);
        }

        // Decides the proposal only from where it was read, so of two concurrent decisions the second changes nothing.
        var decided = await context.RuleCheckProposals
            .Where(row => row.Id == proposal.Id && row.State == RuleProposalState.Proposed)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(row => row.State, RuleProposalState.Confirmed)
                    .SetProperty(row => row.CheckId, check.Id)
                    .SetProperty(row => row.CheckName, check.Name),
                cancellationToken);
        if (decided == 0)
        {
            return Result<RuleCheckProposal>.Failure([Decided(slug, number)]);
        }

        await auditLog.AppendAsync(new NewAuditEvent("rule.check_confirmed") { SubjectType = SubjectType, SubjectId = rule.Id }, cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return Result<RuleCheckProposal>.Success(ToProposal(proposal) with { CheckName = check.Name, State = RuleProposalState.Confirmed });
    }

    public async Task<Result<RuleCheckProposal>> RejectAsync(string slug, int number, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slug);

        if (PeopleOnly("reject a check for") is { } refused)
        {
            return Result<RuleCheckProposal>.Failure([refused]);
        }

        var found = await UndecidedAsync(slug, number, cancellationToken);
        if (!found.IsSuccess)
        {
            return Result<RuleCheckProposal>.Failure(found.Errors);
        }

        var (rule, proposal) = found.Value;
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var decided = await context.RuleCheckProposals
            .Where(row => row.Id == proposal.Id && row.State == RuleProposalState.Proposed)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.State, RuleProposalState.Rejected), cancellationToken);
        if (decided == 0)
        {
            return Result<RuleCheckProposal>.Failure([Decided(slug, number)]);
        }

        await auditLog.AppendAsync(new NewAuditEvent("rule.check_rejected") { SubjectType = SubjectType, SubjectId = rule.Id }, cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return Result<RuleCheckProposal>.Success(ToProposal(proposal) with { State = RuleProposalState.Rejected });
    }

    public async Task<Result<RuleDefinition>> CompilingAsync(Guid ruleId, int version, Guid jobId, CancellationToken cancellationToken = default)
    {
        if (EngineOnly() is { } refused)
        {
            return Result<RuleDefinition>.Failure([refused]);
        }

        var stored = await (
                from compilation in context.RuleCompilations.AsNoTracking()
                join definition in context.RuleVersions.AsNoTracking()
                    on new { compilation.RuleId, compilation.Version } equals new { definition.RuleId, definition.Version }
                where compilation.RuleId == ruleId && compilation.Version == version
                select new { compilation.Status, compilation.JobId, definition.Definition })
            .SingleOrDefaultAsync(cancellationToken);

        return stored is { Status: RuleCompileStatus.Compiling } && stored.JobId == jobId
            ? RuleDefinition.Parse(stored.Definition)
            : Result<RuleDefinition>.Failure([Replaced(version)]);
    }

    public async Task<Result> RecordAsync(Guid ruleId, int version, Guid jobId, RuleCompileOutcome outcome, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        if (EngineOnly() is { } refused)
        {
            return Result.Failure([refused]);
        }

        var rule = await context.Rules.AsNoTracking().SingleOrDefaultAsync(row => row.Id == ruleId, cancellationToken);
        if (rule is null || !await CompilingQuery(ruleId, version, jobId).AnyAsync(cancellationToken))
        {
            return Result.Failure([Replaced(version)]);
        }

        var next = (await context.RuleCheckProposals.Where(row => row.RuleId == ruleId).MaxAsync(row => (int?)row.Number, cancellationToken) ?? 0) + 1;
        var status = outcome.Status == RuleCompileStatus.Proposed && outcome.Proposals.Count > 0 ? RuleCompileStatus.Proposed : RuleCompileStatus.Advisory;
        var message = outcome.Message is { Length: > RuleCompilationRow.MaxMessageLength } text ? text[..RuleCompilationRow.MaxMessageLength] : outcome.Message;

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        // Moves the compile on only while this job is still the version's; a request to propose again replaces it.
        var moved = await CompilingQuery(ruleId, version, jobId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Status, status).SetProperty(row => row.Message, message), cancellationToken);
        if (moved == 0)
        {
            return Result.Failure([Replaced(version)]);
        }

        if (status == RuleCompileStatus.Proposed)
        {
            foreach (var proposal in outcome.Proposals)
            {
                context.RuleCheckProposals.Add(new RuleCheckProposalRow
                {
                    Id = Guid.CreateVersion7(),
                    RuleId = ruleId,
                    RuleVersion = version,
                    Number = next,
                    CheckName = CheckName(rule.Slug, next),
                    Definition = proposal.ToJson(),
                    State = RuleProposalState.Proposed,
                });
                next++;
            }
        }

        await auditLog.AppendAsync(
            new NewAuditEvent("rule.compiled") { SubjectType = SubjectType, SubjectId = ruleId, Model = outcome.Model, PromptHash = outcome.PromptHash },
            cancellationToken);

        // Two compiles of one rule that number their proposals alike conflict here, and the job's next attempt numbers them again.
        await scope.CommitAsync(cancellationToken);
        return Result.Success();
    }

    // Starts proposing checks for a version inside the caller's unit of work: a compile job when a model is configured, and otherwise
    // the version is advisory at once (MDL-03). replace is false for a version just written, which has no compile yet.
    internal async Task ScheduleAsync(Guid ruleId, int version, bool replace, CancellationToken cancellationToken)
    {
        Guid? jobId = null;
        if (compiler.IsAvailable)
        {
            var job = await jobs.EnqueueAsync(
                new NewJob(RuleCompiler.JobType, JsonSerializer.SerializeToElement(new { ruleId, version })) { Subject = new JobSubject(SubjectType, ruleId) },
                cancellationToken);
            jobId = job.Id;
        }

        var status = jobId is null ? RuleCompileStatus.Advisory : RuleCompileStatus.Compiling;
        var message = jobId is null ? RuleCompiler.NoModel : null;
        var replaced = replace
            ? await context.RuleCompilations
                .Where(row => row.RuleId == ruleId && row.Version == version)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(row => row.Status, status).SetProperty(row => row.Message, message).SetProperty(row => row.JobId, jobId),
                    cancellationToken)
            : 0;

        if (replaced == 0)
        {
            context.RuleCompilations.Add(new RuleCompilationRow
            {
                Id = Guid.CreateVersion7(),
                RuleId = ruleId,
                Version = version,
                Status = status,
                Message = message,
                JobId = jobId,
            });
        }
    }

    // A rule's proposals are named after it, numbered so each rule's are distinct, and cut to fit a check name.
    internal static string CheckName(string slug, int number)
    {
        var suffix = $"-{number}";
        var head = slug.Length + suffix.Length > SchemaNames.MaxLength ? slug[..(SchemaNames.MaxLength - suffix.Length)].TrimEnd('-') : slug;
        return head + suffix;
    }

    private IQueryable<RuleCompilationRow> CompilingQuery(Guid ruleId, int version, Guid jobId)
    {
        return context.RuleCompilations.Where(row =>
            row.RuleId == ruleId && row.Version == version && row.JobId == jobId && row.Status == RuleCompileStatus.Compiling);
    }

    private async Task<Result<(RuleRow Rule, RuleCheckProposalRow Proposal)>> UndecidedAsync(string slug, int number, CancellationToken cancellationToken)
    {
        var rule = await FindAsync(slug, cancellationToken);
        if (rule is null)
        {
            return Result<(RuleRow, RuleCheckProposalRow)>.Failure([NotFound(slug)]);
        }

        var proposal = await context.RuleCheckProposals.AsNoTracking()
            .SingleOrDefaultAsync(row => row.RuleId == rule.Id && row.Number == number, cancellationToken);
        if (proposal is null)
        {
            return Result<(RuleRow, RuleCheckProposalRow)>.Failure(
                RuleErrorCodes.ProposalNotFound,
                $"The rule '{slug}' has no proposed check {number}; read the rule's proposals for the numbers it has.");
        }

        return proposal.State == RuleProposalState.Proposed
            ? Result<(RuleRow, RuleCheckProposalRow)>.Success((rule, proposal))
            : Result<(RuleRow, RuleCheckProposalRow)>.Failure([Decided(slug, number)]);
    }

    // Deciding which checks guard content is a person's (D17): no agent, whatever its scope, and no engine.
    private Error? PeopleOnly(string action)
    {
        return currentInitiator.Current switch
        {
            AgentInitiator => new Error(RuleErrorCodes.PeopleOnly, $"Only a person can {action} a rule; an agent reads the rule book and cannot."),
            SystemInitiator system => new Error(RuleErrorCodes.PeopleOnly, $"The '{system.Component}' engine cannot {action} a rule; a person does."),
            _ => null,
        };
    }

    // What a compile proposed is recorded only by the check engine's job, never by a caller who could make proposals up.
    private Error? EngineOnly()
    {
        return currentInitiator.Current == SystemInitiator.Checks
            ? null
            : new Error(RuleErrorCodes.EngineOnly, "Only the check engine records what a rule's compile proposed; propose checks for the rule instead.");
    }

    private Task<RuleRow?> FindAsync(string slug, CancellationToken cancellationToken)
    {
        return context.Rules.AsNoTracking().SingleOrDefaultAsync(row => row.SiteId == SiteId && row.Slug == slug, cancellationToken);
    }

    private static RuleCheckProposal ToProposal(RuleCheckProposalRow row)
    {
        var definition = CheckDefinition.Parse(row.Definition);
        return definition.IsSuccess
            ? new RuleCheckProposal(row.Number, row.RuleVersion, row.CheckName, definition.Value, row.State)
            : throw new InvalidOperationException($"Proposed check {row.Number} cannot be read: {definition.Errors[0].Message}");
    }

    private static Error NotFound(string slug)
    {
        return new Error(RuleErrorCodes.NotFound, $"There is no rule '{slug}'; list the rule book for the rules it holds.");
    }

    private static Error VersionNotFound(RuleRow rule, int version)
    {
        return new Error(RuleErrorCodes.VersionNotFound, $"The rule '{rule.Slug}' has no version {version}; its latest version is {rule.Version}.");
    }

    private static Error Decided(string slug, int number)
    {
        return new Error(RuleErrorCodes.ProposalDecided, $"Proposed check {number} of the rule '{slug}' has already been confirmed, rejected or replaced.");
    }

    private static Error Replaced(int version)
    {
        return new Error(RuleErrorCodes.CompileReplaced, $"Checks for version {version} were proposed again since this compile began, so it records nothing.");
    }
}
