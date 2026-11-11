using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentCMS.Authorization;
using FluentCMS.Content;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Plugins.AiAssist;

// The query parameters of GET /ai/usage, as they were sent.
public sealed record AiUsageQuery(string? PrincipalId, string? Action, string? From, string? To, string? Page, string? PageSize);

public sealed record AiUsageRow(
    Guid Id,
    string PrincipalKind,
    Guid PrincipalId,
    string Action,
    string Type,
    string Field,
    string Provider,
    string Model,
    long PromptTokens,
    long CompletionTokens,
    long DurationMs,
    bool Succeeded,
    DateTimeOffset CreatedAt);

// The tokens of every row the filter matches, not only of the page.
public sealed record AiUsageTotals(long Calls, long PromptTokens, long CompletionTokens);

public sealed record AiUsagePage(IReadOnlyList<AiUsageRow> Items, long Total, int Page, int PageSize, AiUsageTotals Totals);

// The usage log of the plugin: a row for every call that was sent to a provider, and the page of them that
// GET /ai/usage answers for the site of the current operation, which needs ai.usage.read there.
internal sealed partial class AiUsageService(
    PluginDbContext<AiAssistPlugin> database,
    IPermissionChecker permissions,
    IOperationContextAccessor operations)
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;

    public static string KindOf(PrincipalKind kind) => JsonNamingPolicy.CamelCase.ConvertName(kind.ToString());

    // Not cancelled with the request: a call that was sent is logged even when its caller has gone. The row is let go
    // again, so that one that could not be written is not tried with the next.
    public async Task RecordAsync(AiUsage usage)
    {
        database.Add(usage);
        try
        {
            await database.SaveChangesAsync(CancellationToken.None);
        }
        finally
        {
            database.Entry(usage).State = EntityState.Detached;
        }
    }

    public async Task<AiUsagePage> QueryAsync(AiUsageQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var operation = operations.Current ?? throw new InvalidOperationException("The AI usage of a site is read inside an operation, which names the site.");
        var site = operation.Site;
        await permissions.RequireAsync(AiPermissions.ReadUsage, site, cancellationToken);

        List<ValidationError> errors = [];
        var principal = Parse<Guid>(query.PrincipalId, "principalId", "an id", Guid.TryParse, errors);
        var from = Parse<DateTimeOffset>(query.From, "from", "a date and time with an offset, such as 2026-11-01T00:00:00Z", TryParseTime, errors);
        var to = Parse<DateTimeOffset>(query.To, "to", "a date and time with an offset, such as 2026-11-01T00:00:00Z", TryParseTime, errors);
        var page = Parse<int>(query.Page, "page", "a number from 1", (string text, out int value) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 1, errors) ?? 1;
        var pageSize = Parse<int>(query.PageSize, "pageSize", $"a number from 1 to {MaxPageSize}", (string text, out int value) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value is >= 1 and <= MaxPageSize, errors) ?? DefaultPageSize;
        var action = string.IsNullOrWhiteSpace(query.Action) ? null : query.Action.Trim();
        if (action is not null && !AiActions.IsKnown(action))
        {
            errors.Add(new ValidationError("action", AiErrorCodes.InvalidRequest, $"An action is one of {string.Join(", ", AiActions.Names)}."));
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var rows = database.Set<AiUsage>().AsNoTracking().Where(usage => usage.SiteId == site.Value);
        if (principal is { } principalId)
        {
            rows = rows.Where(usage => usage.PrincipalId == principalId);
        }

        if (action is not null)
        {
            rows = rows.Where(usage => usage.Action == action);
        }

        // In UTC, as the rows are written: PostgreSQL takes no other offset for a timestamp with time zone.
        if (from?.ToUniversalTime() is { } since)
        {
            rows = rows.Where(usage => usage.CreatedAt >= since);
        }

        if (to?.ToUniversalTime() is { } until)
        {
            rows = rows.Where(usage => usage.CreatedAt < until);
        }

        var total = await rows.LongCountAsync(cancellationToken);
        var totals = new AiUsageTotals(
            total,
            await rows.SumAsync(usage => usage.PromptTokens, cancellationToken),
            await rows.SumAsync(usage => usage.CompletionTokens, cancellationToken));
        var items = await rows
            .OrderByDescending(usage => usage.CreatedAt)
            .ThenByDescending(usage => usage.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new AiUsagePage([.. items.Select(Row)], total, page, pageSize, totals);
    }

    private delegate bool TryParse<T>(string text, out T value);

    private static T? Parse<T>(string? text, string path, string shape, TryParse<T> parse, List<ValidationError> errors)
        where T : struct
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (parse(text.Trim(), out var value))
        {
            return value;
        }

        errors.Add(new ValidationError(path, AiErrorCodes.InvalidRequest, $"'{path}' is {shape}."));
        return null;
    }

    // A time without an offset would be read in the zone of the server, which the caller does not know.
    private static bool TryParseTime(string text, out DateTimeOffset value)
    {
        value = default;
        return Offset().IsMatch(text) && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    private static AiUsageRow Row(AiUsage usage) => new(
        usage.Id,
        usage.PrincipalKind,
        usage.PrincipalId,
        usage.Action,
        usage.Type,
        usage.Field,
        usage.Provider,
        usage.Model,
        usage.PromptTokens,
        usage.CompletionTokens,
        usage.DurationMs,
        usage.Succeeded,
        usage.CreatedAt);

    [GeneratedRegex(@"(Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex Offset();
}
