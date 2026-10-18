using System.Runtime.CompilerServices;
using FluentCMS.Core.Audit;
using FluentCMS.Core.Results;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.Audit;

internal sealed class AuditReader(FluentCmsDbContext context) : IAuditReader
{
    public Result<IAsyncEnumerable<AuditEvent>> Read(AuditQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var valid = query.Validate();
        return valid.IsSuccess
            ? Result<IAsyncEnumerable<AuditEvent>>.Success(StreamAsync(query))
            : Result<IAsyncEnumerable<AuditEvent>>.Failure(valid.Errors);
    }

    private async IAsyncEnumerable<AuditEvent> StreamAsync(AuditQuery query, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var row in Select(query).AsNoTracking().AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            yield return row.ToEvent();
        }
    }

    // SQL rather than LINQ because EF Core will not compare or order a DateTimeOffset on SQLite, which stores it as text. Every
    // stored timestamp is UTC (the log stamps them from TimeProvider.GetUtcNow), and within one offset that text sorts in time
    // order, so one statement serves both providers. The window and subject use the Timestamp and SubjectType, SubjectId indexes.
    private IQueryable<AuditEventRow> Select(AuditQuery query)
    {
        var conditions = new List<string>();
        var parameters = new List<object>();

        // Column and operator are fixed below; every value travels as a parameter.
        void Where(string column, string comparison, object value)
        {
            conditions.Add($"\"{column}\" {comparison} {{{parameters.Count}}}");
            parameters.Add(value);
        }

        if (query.From is { } from)
        {
            Where("Timestamp", ">=", from.ToUniversalTime());
        }

        if (query.To is { } to)
        {
            Where("Timestamp", "<", to.ToUniversalTime());
        }

        if (query.Subject is { } subject)
        {
            Where("SubjectType", "=", subject.Type);
            Where("SubjectId", "=", subject.Id);
        }

        var filter = conditions.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", conditions);
        return context.AuditEvents.FromSql(FormattableStringFactory.Create(
            "SELECT * FROM \"AuditEvents\"" + filter + " ORDER BY \"Timestamp\", \"Id\"",
            [.. parameters]));
    }
}
