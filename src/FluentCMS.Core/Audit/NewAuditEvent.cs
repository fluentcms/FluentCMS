namespace FluentCMS.Core.Audit;

// The part of an audit event a service supplies; the log adds the id, the time and the initiator.
public sealed record NewAuditEvent(string Operation)
{
    public string? SubjectType { get; init; }

    public Guid? SubjectId { get; init; }

    public string? Model { get; init; }

    public string? PromptHash { get; init; }

    public IReadOnlyList<string> Sources { get; init; } = [];

    public Guid? ChangeSetId { get; init; }

    public Guid? PreviousVersionId { get; init; }
}
