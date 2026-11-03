using FluentCMS.Content;

namespace FluentCMS.Activity;

/// <summary>Which rows of the activity log to read. A row must match every filter that is set.</summary>
public sealed record ActivityFilter
{
    /// <summary>Only what this actor did.</summary>
    public Guid? ActorId { get; init; }

    /// <summary>Only what actors of this kind did.</summary>
    public PrincipalKind? ActorKind { get; init; }

    /// <summary>Only operations that came from here.</summary>
    public Origin? Origin { get; init; }

    /// <summary>Only this action, or with a trailing <c>*</c> every action that starts with the text before it, such as <c>content.*</c>.</summary>
    public string? Action { get; init; }

    /// <summary>Only what happened to this kind of thing.</summary>
    public string? TargetType { get; init; }

    /// <summary>Only what happened to the thing with this id.</summary>
    public string? TargetId { get; init; }

    /// <summary>Only what happened at or after this time.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Only what happened before this time.</summary>
    public DateTimeOffset? To { get; init; }

    /// <summary>Whether to include the rows that belong to no site, such as the failures of jobs. The rows of the site of the operation are always included.</summary>
    public bool IncludeInstance { get; init; }
}
