using FluentCMS.Sites;

namespace FluentCMS.Jobs;

/// <summary>How to run a job that is being enqueued.</summary>
public sealed record JobOptions
{
    /// <summary>The queue, or <see langword="null"/> for the default queue.</summary>
    public string? Queue { get; init; }

    /// <summary>The priority. A higher one runs first.</summary>
    public int Priority { get; init; }

    /// <summary>The attempts before the job is dead-lettered, or <see langword="null"/> for the runner's default.</summary>
    public int? MaxAttempts { get; init; }

    /// <summary>The site the job is for, or <see langword="null"/> for the site of the current operation.</summary>
    public SiteId? Site { get; init; }
}
