using FluentCMS.Core.Results;

namespace FluentCMS.Core.Audit;

// A part of the log: an optional window from From (inclusive) to To (exclusive), and optionally one item's history (AUD-03, AUD-N4).
// With neither, it is the full log.
public sealed record AuditQuery
{
    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    public AuditSubject? Subject { get; init; }

    public Result Validate()
    {
        var errors = new List<Error>();

        if (From > To)
        {
            errors.Add(new Error(AuditErrorCodes.WindowInverted, $"The window starts at {From:O}, after it ends at {To:O}."));
        }

        if (Subject is { } subject)
        {
            if (string.IsNullOrWhiteSpace(subject.Type))
            {
                errors.Add(new Error(AuditErrorCodes.SubjectTypeMissing, "An item's history needs the item's type."));
            }

            if (subject.Id == Guid.Empty)
            {
                errors.Add(new Error(AuditErrorCodes.SubjectIdMissing, "An item's history needs the item's id."));
            }
        }

        return errors.Count == 0 ? Result.Success() : Result.Failure(errors);
    }
}

// The item an event happened to, as AuditEvent.SubjectType and SubjectId name it.
public sealed record AuditSubject(string Type, Guid Id);
