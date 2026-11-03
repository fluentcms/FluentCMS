using FluentCMS.Content;

namespace FluentCMS.Activity;

/// <summary>One row of the activity log.</summary>
/// <param name="Id">The id of the row.</param>
/// <param name="SiteId">The site it happened on, or <see langword="null"/> for the instance as a whole.</param>
/// <param name="OccurredAt">When it happened.</param>
/// <param name="ActorKind">The kind of the actor.</param>
/// <param name="ActorId">The id of the actor, which is empty for the system.</param>
/// <param name="ActorName">The name the actor had at the time.</param>
/// <param name="Origin">Where the operation came from.</param>
/// <param name="ClientId">The OAuth client id or API key id the operation used, when there was one.</param>
/// <param name="Action">What happened.</param>
/// <param name="TargetType">The kind of thing it happened to.</param>
/// <param name="TargetId">The id of the thing it happened to.</param>
/// <param name="TargetLabel">A name for the target.</param>
/// <param name="Locale">The locale the action is about.</param>
/// <param name="Details">More facts, by name.</param>
/// <param name="CorrelationId">The correlation id of the operation, which ties the row to the others of the same request or job.</param>
/// <param name="Ip">The network address the action came from, when it was known.</param>
public sealed record ActivityItem(
    Guid Id,
    Guid? SiteId,
    DateTimeOffset OccurredAt,
    PrincipalKind ActorKind,
    Guid ActorId,
    string ActorName,
    Origin Origin,
    string? ClientId,
    string Action,
    string? TargetType,
    string? TargetId,
    string? TargetLabel,
    string? Locale,
    IReadOnlyDictionary<string, string?> Details,
    string CorrelationId,
    string? Ip);
