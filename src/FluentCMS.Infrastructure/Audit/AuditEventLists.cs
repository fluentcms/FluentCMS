using System.Runtime.CompilerServices;
using FluentCMS.Core.Audit;
using FluentCMS.Core.Results;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.Audit;

// SQL rather than LINQ because EF Core will not compare or order a DateTimeOffset on SQLite, which stores it as text; stored
// timestamps are all UTC, so the text sorts in time order, as in AuditReader. Every value travels as a parameter.
internal sealed class AuditEventLists(FluentCmsDbContext context) : IAuditEventLists
{
    public async Task<Result<AuditEventList>> ListAsync(AuditEventListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var valid = query.Validate();
        if (!valid.IsSuccess)
        {
            return Result<AuditEventList>.Failure(valid.Errors);
        }

        var (filter, parameters) = Filter(query);
        var limit = Parameter(parameters, query.Limit);
        var offset = Parameter(parameters, query.Offset);
        var rows = await context.AuditEvents
            .FromSql(FormattableStringFactory.Create(
                $"SELECT * FROM \"AuditEvents\" WHERE {filter} ORDER BY \"Timestamp\" DESC, \"Id\" DESC LIMIT {limit} OFFSET {offset}",
                [.. parameters]))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var total = await CountRowsAsync(query, cancellationToken);
        return Result<AuditEventList>.Success(new AuditEventList([.. rows.Select(row => row.ToEvent())], total));
    }

    public async Task<Result<int>> CountAsync(AuditEventListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var valid = query.Validate();
        return valid.IsSuccess
            ? Result<int>.Success(await CountRowsAsync(query, cancellationToken))
            : Result<int>.Failure(valid.Errors);
    }

    private Task<int> CountRowsAsync(AuditEventListQuery query, CancellationToken cancellationToken)
    {
        var (filter, parameters) = Filter(query);
        return context.AuditEvents
            .FromSql(FormattableStringFactory.Create($"SELECT * FROM \"AuditEvents\" WHERE {filter}", [.. parameters]))
            .CountAsync(cancellationToken);
    }

    private static (string Filter, List<object> Parameters) Filter(AuditEventListQuery query)
    {
        var parameters = new List<object>();
        var operations = string.Join(", ", query.Operations.Distinct().Select(operation => Parameter(parameters, operation)));
        var filter = $"\"Operation\" IN ({operations})";

        if (query.Since is { } since)
        {
            filter += $" AND \"Timestamp\" >= {Parameter(parameters, since.ToUniversalTime())}";
        }

        return (filter, parameters);
    }

    private static string Parameter(List<object> parameters, object value)
    {
        parameters.Add(value);
        return $"{{{parameters.Count - 1}}}";
    }
}
