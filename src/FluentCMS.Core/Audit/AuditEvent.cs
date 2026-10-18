using FluentCMS.Core.Identity;

namespace FluentCMS.Core.Audit;

// One record of an operation, appended and never edited: who acted, with which model and prompt, from which sources, in which change set (AUD-01).
public sealed record AuditEvent
{
    public required Guid Id
    {
        get;
        init => field = value != Guid.Empty ? value : throw new ArgumentException("An audit event needs an id.", nameof(Id));
    }

    public required DateTimeOffset Timestamp { get; init; }

    public required Initiator Initiator
    {
        get;
        init => field = value ?? throw new ArgumentNullException(nameof(Initiator));
    }

    // What happened, as a dotted lower-case name such as "user.created".
    public required string Operation
    {
        get;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(Operation));
            field = value;
        }
    }

    // What it happened to; null until an epic defines the item kind.
    public string? SubjectType { get; init; }

    public Guid? SubjectId { get; init; }

    public string? Model { get; init; }

    public string? PromptHash { get; init; }

    public IReadOnlyList<string> Sources
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value, nameof(Sources));
            if (value.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException("A source cannot be blank.", nameof(Sources));
            }

            field = value;
        }
    } = [];

    public Guid? ChangeSetId { get; init; }

    public Guid? PreviousVersionId { get; init; }

    // The credential an agent acted under; null when a person acted (AUD-01, SEC-01).
    public Guid? AgentCredentialId => (Initiator as AgentInitiator)?.CredentialId;
}
