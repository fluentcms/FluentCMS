using FluentCMS.Core.Results;

namespace FluentCMS.Core.Audit;

// Pages of the log for screens, newest first, such as the desk's security events (SEC-08). IAuditReader streams the log oldest first
// for exports; this reads one page and the total. A list appends no event.
public interface IAuditEventLists
{
    Task<Result<AuditEventList>> ListAsync(AuditEventListQuery query, CancellationToken cancellationToken = default);

    // How many events the query selects, whatever its offset and limit.
    Task<Result<int>> CountAsync(AuditEventListQuery query, CancellationToken cancellationToken = default);
}

public sealed record AuditEventList(IReadOnlyList<AuditEvent> Items, int Total);

// The events with any of the given operations, optionally only those at or after a time, newest first.
public sealed record AuditEventListQuery
{
    public const int MaxLimit = 200;

    public required IReadOnlyCollection<string> Operations { get; init; }

    public DateTimeOffset? Since { get; init; }

    public int Offset { get; init; }

    public int Limit { get; init; } = 50;

    public Result Validate()
    {
        var errors = new List<Error>();

        if (Operations is null || Operations.Count == 0 || Operations.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add(new Error(AuditErrorCodes.OperationsMissing, "A list names one or more operations to list."));
        }

        if (Offset < 0)
        {
            errors.Add(new Error(AuditErrorCodes.OffsetInvalid, $"The offset is {Offset}; it is 0 or more."));
        }

        if (Limit is < 1 or > MaxLimit)
        {
            errors.Add(new Error(AuditErrorCodes.LimitInvalid, $"The limit is {Limit}; it is from 1 to {MaxLimit}."));
        }

        return errors.Count == 0 ? Result.Success() : Result.Failure(errors);
    }
}
