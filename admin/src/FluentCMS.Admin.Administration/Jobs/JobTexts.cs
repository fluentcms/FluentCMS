using FluentCMS.Admin.Api;
using Microsoft.Extensions.Localization;

namespace FluentCMS.Admin.Administration.Jobs;

internal enum JobSectionState
{
    Loading,
    Ready,
    Failed,
}

// What the pages of the area say of a job, and when they follow one (spec 2.7).
internal static class JobTexts
{
    private const string TimedOutPrefix = JobProblems.AttemptTimedOut + ": ";

    // A followed job is read again this long after its last read, at most, while it shows.
    public static readonly TimeSpan FollowEvery = TimeSpan.FromSeconds(5);

    private static readonly string[] Outcomes = ["succeeded", "failed", "lease_expired", "released"];

    // The job's page follows a running job, which changes by itself, and a pending one that is due, which a worker takes next,
    // as after a retry. A row of the list follows only a running job, so that a page of due jobs is not a read each per row.
    public static bool Follows(JobRecord job, DateTimeOffset now) =>
        job.Status == "running" || (job.Status == "pending" && job.RunAt <= now);

    public static string Status(string status, IStringLocalizer<JobsResources> texts) =>
        JobsApi.Statuses.Contains(status, StringComparer.Ordinal) ? texts[$"Jobs.Status.{status}"] : status;

    public static string Outcome(string? outcome, IStringLocalizer<JobsResources> texts) =>
        outcome is null ? texts["Jobs.Outcome.None"]
        : Outcomes.Contains(outcome, StringComparer.Ordinal) ? texts[$"Jobs.Outcome.{outcome}"]
        : outcome;

    public static string Attempts(JobRecord job, IStringLocalizer<JobsResources> texts) =>
        texts["Jobs.Job.AttemptsOf", job.Attempts, job.MaxAttempts];

    // An attempt abandoned past its timeout is failed with the runner's code at the start of its error, which says so in the
    // admin's words with the rest of the error.
    public static string? Error(string? error, IStringLocalizer<JobsResources> texts) =>
        error is not null && error.StartsWith(TimedOutPrefix, StringComparison.Ordinal) ? texts["Jobs.Job.TimedOut", error[TimedOutPrefix.Length..]] : error;

    // A site by the name the shell knows it by, else by its id; a job of no site is the instance's.
    public static string Site(Guid? site, IReadOnlyList<MySiteInfo> sites, IStringLocalizer<JobsResources> texts) =>
        site is not { } id ? texts["Jobs.Job.Instance"]
        : sites.FirstOrDefault(item => item.Id == id)?.Name ?? id.ToString("D");
}
