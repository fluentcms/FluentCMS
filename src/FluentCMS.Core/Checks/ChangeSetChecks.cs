using System.Text.Json;
using FluentCMS.Core.Audit;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Content;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Checks;

// Checks read and judge outside any unit of work, since a model may take a while to answer; only the results and the move they
// decide are written, together, as the check engine (ADR-0012). Each submission has one job, and a run belongs to it: a run of an
// earlier submission, or one that another run of the same submission beat, changes nothing.
internal sealed class ChangeSetChecks(
    IChangeSetDirectory changeSets,
    IEntryDirectory entries,
    IContentTypeDirectory contentTypes,
    ICheckDirectory checks,
    CheckTypeRegistry checkTypes,
    ICheckRunner runner,
    ICheckRuns runs,
    IJobQueue jobs,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    IServiceScopeFactory scopes,
    TimeProvider clock) : IChangeSetChecks
{
    // The most items a set may have to be checked on the request that submits it, which keeps that request within PERF-02's ten
    // seconds; larger sets, and any set an LLM-judged check reads, are checked as a job (ADR-0010).
    public const int InlineItems = 50;

    public const string JobType = "checks.run";

    // Enough to ride out a short outage, after which the last attempt fails the set rather than leave it in checks running.
    public const int JobAttempts = 6;

    public const string LeaseLost = "check_run_lease_lost";

    private const string SubjectType = "change_set";

    // How long the job behind an inline run waits before it runs the checks itself, should that run not have finished.
    private static readonly TimeSpan InlineGrace = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(1);

    public async Task<Result<ChangeSetSubmission>> SubmitAsync(Guid changeSetId, CancellationToken cancellationToken = default)
    {
        var read = await changeSets.GetAsync(changeSetId, cancellationToken);
        if (!read.IsSuccess)
        {
            return Result<ChangeSetSubmission>.Failure(read.Errors);
        }

        var inline = read.Value.Operations.Count <= InlineItems && !await AnyJudgedAsync(cancellationToken);

        Job job;
        ChangeSet submitted;
        await using (var work = await unitOfWork.BeginAsync(cancellationToken))
        {
            var moved = await changeSets.TransitionAsync(changeSetId, ChangeSetState.ChecksRunning, cancellationToken);
            if (!moved.IsSuccess)
            {
                // A refusal keeps its security event (SEC-08); a conflict has already rolled the work back, so it cannot commit.
                if (!moved.Errors.Any(error => error.Code == ChangeSetErrorCodes.Conflict))
                {
                    await work.CommitAsync(cancellationToken);
                }

                return Result<ChangeSetSubmission>.Failure(moved.Errors);
            }

            // The job commits with the move, so a set in checks running always has a run coming (ADR-0010).
            job = await jobs.EnqueueAsync(
                new NewJob(JobType, JsonSerializer.SerializeToElement(new { changeSetId }))
                {
                    Subject = new JobSubject(SubjectType, changeSetId),
                    RunAt = inline ? clock.GetUtcNow() + InlineGrace : null,
                    MaxAttempts = JobAttempts,
                },
                cancellationToken);
            await work.CommitAsync(cancellationToken);
            submitted = moved.Value;
        }

        if (!inline)
        {
            return Result<ChangeSetSubmission>.Success(new ChangeSetSubmission(submitted, null, job));
        }

        // The set is submitted now whatever becomes of the request, so the run is not cancelled with it; if it does not finish, the
        // job checks the set instead.
        await using var engine = scopes.CreateScopeActingAs(SystemInitiator.Checks);
        var ran = await engine.ServiceProvider.GetRequiredService<IChangeSetChecks>()
            .RunAsync(changeSetId, job.Id, CheckRunMode.Inline, null, CancellationToken.None);

        var after = await changeSets.GetAsync(changeSetId, CancellationToken.None);
        return Result<ChangeSetSubmission>.Success(new ChangeSetSubmission(
            after.IsSuccess ? after.Value : submitted,
            ran.IsSuccess ? ran.Value : null,
            job));
    }

    public async Task<Result<ChangeSetCheckRun?>> RunAsync(
        Guid changeSetId,
        Guid jobId,
        CheckRunMode mode,
        Func<int, int, Task<bool>>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var current = await CurrentAsync(changeSetId, jobId, cancellationToken);
        if (!current.IsSuccess || current.Value is not { } set)
        {
            return current.IsSuccess ? Result<ChangeSetCheckRun?>.Success(await runs.LatestAsync(changeSetId, cancellationToken)) : Result<ChangeSetCheckRun?>.Failure(current.Errors);
        }

        // Read once, so every item is judged by the same checks even if one is activated during the run.
        var started = clock.GetUtcNow();
        var active = await checks.ListActiveAsync(cancellationToken);
        var (subjects, problems) = await SubjectsAsync(set, cancellationToken);
        var chargedTo = (set.Initiator as AgentInitiator)?.CredentialId;
        var items = new List<ItemChecks>();
        var done = 0;
        var reported = started;

        foreach (var (operation, contentType, subject) in subjects)
        {
            // An item no check applies to is left out, as it is when the run is read back.
            var itemRun = await runner.RunAsync(subject, active, CheckSelection.All, chargedTo, cancellationToken);
            if (itemRun.Results.Count > 0)
            {
                items.Add(new ItemChecks(operation.ItemKind, operation.ItemId, operation.ProposedVersion!.Value, contentType, itemRun.Results));
            }

            // Progress is written at most once a second, and once at the end, so a large set is not slowed by its own reports.
            done++;
            if (progress is null || (done < subjects.Count && clock.GetUtcNow() - reported < ProgressInterval))
            {
                continue;
            }

            reported = clock.GetUtcNow();
            if (!await progress(done, subjects.Count))
            {
                return Result<ChangeSetCheckRun?>.Failure(LeaseLost, $"The check run of change set {changeSetId} stopped: another worker has its job.");
            }
        }

        var results = items.SelectMany(item => item.Results).ToList();
        var status = problems.Count > 0 ? CheckStatus.Fail : CheckResult.Overall(results);
        var run = new ChangeSetCheckRun(
            Guid.CreateVersion7(),
            changeSetId,
            0,
            mode,
            started,
            clock.GetUtcNow(),
            status,
            results.Sum(result => result.Model?.Cost ?? 0),
            items,
            problems);

        return await FinishAsync(run, jobId, cancellationToken);
    }

    public async Task<Result<ChangeSetCheckRun?>> AbandonAsync(Guid changeSetId, Guid jobId, CheckRunMode mode, CancellationToken cancellationToken = default)
    {
        var current = await CurrentAsync(changeSetId, jobId, cancellationToken);
        if (!current.IsSuccess || current.Value is null)
        {
            return current.IsSuccess ? Result<ChangeSetCheckRun?>.Success(await runs.LatestAsync(changeSetId, cancellationToken)) : Result<ChangeSetCheckRun?>.Failure(current.Errors);
        }

        var now = clock.GetUtcNow();
        return await FinishAsync(
            new ChangeSetCheckRun(
                Guid.CreateVersion7(),
                changeSetId,
                0,
                mode,
                now,
                now,
                CheckStatus.Fail,
                0,
                [],
                [$"The checks could not run on this change set. Reopen it and submit it again; if they still cannot run, ask a person to look at job {jobId}."]),
            jobId,
            cancellationToken);
    }

    public async Task<Result<ChangeSetChecksState>> GetAsync(Guid changeSetId, CancellationToken cancellationToken = default)
    {
        var read = await changeSets.GetAsync(changeSetId, cancellationToken);
        if (!read.IsSuccess)
        {
            return Result<ChangeSetChecksState>.Failure(read.Errors);
        }

        var run = await runs.LatestAsync(changeSetId, cancellationToken);
        return Result<ChangeSetChecksState>.Success(new ChangeSetChecksState(read.Value, run, await LatestJobAsync(changeSetId, cancellationToken)));
    }

    // The set when this job's submission is the one in checks running, and null when the set has moved on or been submitted again.
    private async Task<Result<ChangeSet?>> CurrentAsync(Guid changeSetId, Guid jobId, CancellationToken cancellationToken)
    {
        var read = await changeSets.GetAsync(changeSetId, cancellationToken);
        if (!read.IsSuccess)
        {
            return Result<ChangeSet?>.Failure(read.Errors);
        }

        var current = read.Value.State == ChangeSetState.ChecksRunning && (await LatestJobAsync(changeSetId, cancellationToken))?.Id == jobId;
        return Result<ChangeSet?>.Success(current ? read.Value : null);
    }

    // Records the run and makes the move it decides in one unit of work, which commits only if the run's submission is still the
    // set's: the job is checked after the move, whose row lock makes a submission that commits first visible to it.
    private async Task<Result<ChangeSetCheckRun?>> FinishAsync(ChangeSetCheckRun run, Guid jobId, CancellationToken cancellationToken)
    {
        var changeSetId = run.ChangeSetId;
        Result<ChangeSet> moved;
        var committed = false;
        await using (var work = await unitOfWork.BeginAsync(cancellationToken))
        {
            await runs.RecordAsync(run, cancellationToken);
            await auditLog.AppendAsync(new NewAuditEvent("change_set.checked") { SubjectType = SubjectType, SubjectId = changeSetId, ChangeSetId = changeSetId }, cancellationToken);

            // Each judgement names its model and prompt, so the prompt can be read back from the audit log (CHK-05).
            foreach (var result in run.Items.SelectMany(item => item.Results).Where(result => result.Model is not null))
            {
                await auditLog.AppendAsync(
                    new NewAuditEvent("check.judged")
                    {
                        SubjectType = "check",
                        SubjectId = result.CheckId,
                        ChangeSetId = changeSetId,
                        Model = result.Model!.Model,
                        PromptHash = result.Model.PromptHash,
                    },
                    cancellationToken);
            }

            // Routing decides what happens to a set that passes (F-RTE); until it does, every set that passes waits for review.
            var target = run.Status == CheckStatus.Fail ? ChangeSetState.ChecksFailed : ChangeSetState.AwaitingReview;
            moved = await changeSets.TransitionAsync(changeSetId, target, cancellationToken);
            if (moved.IsSuccess && (await LatestJobAsync(changeSetId, cancellationToken))?.Id == jobId)
            {
                await work.CommitAsync(cancellationToken);
                committed = true;
            }
        }

        // Read back as stored, so the run looks the same here as wherever it is read later.
        if (committed)
        {
            return Result<ChangeSetCheckRun?>.Success(await runs.LatestAsync(changeSetId, cancellationToken));
        }

        // Rolled back. A set still in checks running for this submission met a write that another attempt can get past.
        var current = await CurrentAsync(changeSetId, jobId, cancellationToken);
        if (current.IsSuccess && current.Value is not null)
        {
            return Result<ChangeSetCheckRun?>.Failure(moved.IsSuccess ? [new Error(ChangeSetErrorCodes.Conflict, $"Change set {changeSetId} changed during its check run.")] : moved.Errors);
        }

        return Result<ChangeSetCheckRun?>.Success(await runs.LatestAsync(changeSetId, cancellationToken));
    }

    private async Task<Job?> LatestJobAsync(Guid changeSetId, CancellationToken cancellationToken)
    {
        return (await jobs.ListForSubjectAsync(new JobSubject(SubjectType, changeSetId), cancellationToken)).FirstOrDefault(job => job.Type == JobType);
    }

    private async Task<bool> AnyJudgedAsync(CancellationToken cancellationToken)
    {
        return (await checks.ListActiveAsync(cancellationToken)).Any(check => checkTypes.Find(check.Definition.Type)?.Kind == CheckKind.LlmJudged);
    }

    // Every entry and entity the set creates or updates, at the version it proposes, with the fields an update changes. Content
    // types are checked by the schema itself, and a delete leaves nothing to check. An item that cannot be read, such as an entry
    // deleted since it was proposed, is a problem that fails the run rather than one that stops it.
    private async Task<(List<(ChangeOperation Operation, string ContentType, CheckSubject Subject)> Subjects, List<string> Problems)> SubjectsAsync(
        ChangeSet set,
        CancellationToken cancellationToken)
    {
        var subjects = new List<(ChangeOperation, string, CheckSubject)>();
        var problems = new List<string>();
        var types = new Dictionary<(string Slug, int Version), ContentType>();
        foreach (var operation in set.Operations.Where(operation =>
                     operation.ItemKind is ItemKind.Entry or ItemKind.Entity && operation.ProposedVersion is not null))
        {
            // The version a set proposes is most often the entry's latest, which the entry read already holds.
            var entry = await entries.GetAsync(operation.ItemId, cancellationToken);
            var proposed = !entry.IsSuccess
                ? null
                : entry.Value.Latest.Version == operation.ProposedVersion
                    ? Result<EntryVersion>.Success(entry.Value.Latest)
                    : await entries.GetVersionAsync(operation.ItemId, operation.ProposedVersion!.Value, cancellationToken);
            var before = proposed is { IsSuccess: true } && operation.Kind == OperationKind.Update && operation.BaseVersion is { } baseVersion
                ? await entries.GetVersionAsync(operation.ItemId, baseVersion, cancellationToken)
                : null;
            if (Problem(operation, entry.Errors, proposed?.Errors, before?.Errors) is { } unreadable)
            {
                problems.Add(unreadable);
                continue;
            }

            var key = (entry.Value.ContentType, proposed!.Value.ContentTypeVersion);
            if (!types.TryGetValue(key, out var type))
            {
                var read = await contentTypes.GetVersionAsync(key.ContentType, key.ContentTypeVersion, cancellationToken);
                if (Problem(operation, read.Errors) is { } missing)
                {
                    problems.Add(missing);
                    continue;
                }

                type = types[key] = read.Value;
            }

            var changed = before is null ? null : Changed(proposed.Value.Fields, before.Value.Fields);
            subjects.Add((operation, entry.Value.ContentType, new CheckSubject(proposed.Value.Fields, set.Sources, entry.Value.ContentType, type.Definition, changed)));
        }

        return (subjects, problems);
    }

    private static string? Problem(ChangeOperation operation, params IReadOnlyList<Error>?[] errors)
    {
        var error = errors.FirstOrDefault(list => list is { Count: > 0 })?[0];
        return error is null
            ? null
            : $"The {operation.ItemKind.ToString().ToLowerInvariant()} {operation.ItemId} could not be checked: {error.Message} "
                + "Restore it, or reopen the change set and remove its operation.";
    }

    private static List<string> Changed(JsonElement proposed, JsonElement before)
    {
        return proposed.EnumerateObject()
            .Where(field => before.ValueKind != JsonValueKind.Object
                || !before.TryGetProperty(field.Name, out var value)
                || !JsonElement.DeepEquals(value, field.Value))
            .Select(field => field.Name)
            .ToList();
    }
}
