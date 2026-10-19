using System.Runtime.CompilerServices;
using FluentCMS.Core.Audit;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Results;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.ChangeSets;

// The directory appends an event with the set as its subject when it opens the set and on every move, so those events date the set
// without a column of its own. Other events about a set, such as a refused move, are left out. SQL rather than LINQ because EF Core
// will not compare or order a DateTimeOffset on SQLite, which stores it as text; stored timestamps are all UTC, so the text sorts in
// time order, as in AuditReader.
internal sealed class ChangeSetLists(FluentCmsDbContext context) : IChangeSetLists
{
    private const string SubjectType = "change_set";

    public async Task<Result<ChangeSetList>> ListAsync(ChangeSetListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var valid = query.Validate();
        if (!valid.IsSuccess)
        {
            return Result<ChangeSetList>.Failure(valid.Errors);
        }

        var (sql, parameters) = Select(query);
        var order = query.NewestFirst ? "DESC" : "ASC";
        var rows = await context.Database
            .SqlQuery<SummaryRow>(Format(
                $"{sql} ORDER BY \"StateSince\" {order}, \"Id\" {order} LIMIT {Parameter(parameters, query.Limit)} OFFSET {Parameter(parameters, query.Offset)}",
                parameters))
            .ToListAsync(cancellationToken);

        var total = await CountRowsAsync(query, cancellationToken);
        return Result<ChangeSetList>.Success(new ChangeSetList([.. rows.Select(ToSummary)], total));
    }

    public async Task<Result<int>> CountAsync(ChangeSetListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var valid = query.Validate();
        return valid.IsSuccess
            ? Result<int>.Success(await CountRowsAsync(query, cancellationToken))
            : Result<int>.Failure(valid.Errors);
    }

    private Task<int> CountRowsAsync(ChangeSetListQuery query, CancellationToken cancellationToken)
    {
        var (sql, parameters) = Select(query);
        return context.Database
            .SqlQuery<int>(Format($"SELECT CAST(COUNT(*) AS INTEGER) AS \"Value\" FROM ({sql}) AS \"Counted\"", parameters))
            .SingleAsync(cancellationToken);
    }

    // The sets of the site in the query's states with their item count and times, kept to those that entered their state in time.
    // Table and column names are fixed here; every value travels as a parameter.
    private static (string Sql, List<object> Parameters) Select(ChangeSetListQuery query)
    {
        var parameters = new List<object>();
        var openedSubject = Parameter(parameters, SubjectType);
        var opened = Parameter(parameters, ChangeSetMoves.Opened);
        var movedSubject = Parameter(parameters, SubjectType);
        var moves = string.Join(", ", ChangeSetMoves.Operations.Select(move => Parameter(parameters, move)));
        var site = Parameter(parameters, Site.Default.Id);
        var states = string.Join(", ", query.States.Distinct().Select(state => Parameter(parameters, state.ToString())));

        var sql = $"""
            SELECT * FROM (
                SELECT s."Id", s."Rationale", s."InitiatorKind", s."InitiatorId", s."InitiatorName", s."State", s."RevertsChangeSetId",
                    (SELECT CAST(COUNT(*) AS INTEGER) FROM "ChangeSetOperations" o WHERE o."ChangeSetId" = s."Id") AS "ItemCount",
                    (SELECT MIN(e."Timestamp") FROM "AuditEvents" e
                        WHERE e."SubjectType" = {openedSubject} AND e."SubjectId" = s."Id" AND e."Operation" = {opened}) AS "OpenedAt",
                    (SELECT MAX(e."Timestamp") FROM "AuditEvents" e
                        WHERE e."SubjectType" = {movedSubject} AND e."SubjectId" = s."Id" AND e."Operation" IN ({moves})) AS "StateSince"
                FROM "ChangeSets" s
                WHERE s."SiteId" = {site} AND s."State" IN ({states}){OpenedBy(query, parameters)}
            ) AS "Listed"
            """;

        if (query.EnteredSince is { } since)
        {
            sql += $" WHERE \"StateSince\" >= {Parameter(parameters, since.ToUniversalTime())}";
        }

        return (sql, parameters);
    }

    private static string OpenedBy(ChangeSetListQuery query, List<object> parameters)
    {
        if (query.OpenedBy is not { } initiator)
        {
            return string.Empty;
        }

        var (kind, id) = InitiatorKinds.Describe(initiator);
        return $" AND s.\"InitiatorKind\" = {Parameter(parameters, kind)} AND s.\"InitiatorId\" = {Parameter(parameters, id)}";
    }

    private static string Parameter(List<object> parameters, object value)
    {
        parameters.Add(value);
        return $"{{{parameters.Count - 1}}}";
    }

    private static FormattableString Format(string sql, List<object> parameters)
    {
        return FormattableStringFactory.Create(sql, [.. parameters]);
    }

    private static ChangeSetSummary ToSummary(SummaryRow row)
    {
        Initiator initiator = row.InitiatorKind switch
        {
            InitiatorKinds.User => new UserInitiator(row.InitiatorId, row.InitiatorName),
            InitiatorKinds.Agent => new AgentInitiator(row.InitiatorId, row.InitiatorName),
            _ => throw new InvalidOperationException($"Change set {row.Id} names an unknown initiator kind '{row.InitiatorKind}'."),
        };

        // Opening a set appends its first event in the same unit of work, so a set without one means a broken store.
        return new ChangeSetSummary(
            row.Id,
            row.Rationale,
            initiator,
            Enum.Parse<ChangeSetState>(row.State),
            row.ItemCount,
            row.RevertsChangeSetId,
            row.OpenedAt ?? throw new InvalidOperationException($"Change set {row.Id} has no audit event."),
            row.StateSince ?? throw new InvalidOperationException($"Change set {row.Id} has no audit event."));
    }

    private sealed class SummaryRow
    {
        public Guid Id { get; set; }

        public string Rationale { get; set; } = string.Empty;

        public string InitiatorKind { get; set; } = string.Empty;

        public Guid InitiatorId { get; set; }

        public string InitiatorName { get; set; } = string.Empty;

        public string State { get; set; } = string.Empty;

        public Guid? RevertsChangeSetId { get; set; }

        public int ItemCount { get; set; }

        public DateTimeOffset? OpenedAt { get; set; }

        public DateTimeOffset? StateSince { get; set; }
    }
}
