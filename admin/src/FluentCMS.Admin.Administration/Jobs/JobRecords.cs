using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentCMS.Admin.Administration.Common;
using FluentCMS.Admin.Api;

namespace FluentCMS.Admin.Administration.Jobs;

// A job as GET /jobs lists it. Its payload is never shown, since it can hold secrets; its error can name internals of the
// host, which is why every route of the area is for the instance.
internal record JobRecord
{
    public required Guid Id { get; init; }

    public required string Type { get; init; }

    public required string Queue { get; init; }

    public Guid? SiteId { get; init; }

    public required string Status { get; init; }

    public int Attempts { get; init; }

    public int MaxAttempts { get; init; }

    public int Priority { get; init; }

    public DateTimeOffset RunAt { get; init; }

    public string? LastError { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public Guid? CreatedBy { get; init; }

    public string? Origin { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }
}

// A job as GET /jobs/{id}, retry and cancel answer it, with its attempts.
internal sealed record JobDetailRecord : JobRecord
{
    public IReadOnlyList<JobAttemptRecord> History { get; init; } = [];
}

// An attempt of a job; one under way has no outcome yet.
internal sealed record JobAttemptRecord
{
    public int Attempt { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }

    public string? Outcome { get; init; }

    public string? Error { get; init; }
}

internal sealed record JobRecordPage
{
    public required IReadOnlyList<JobRecord> Items { get; init; }

    public required long Total { get; init; }
}

// A job that ran out of attempts, or was cancelled, as GET /jobs/dead-letter lists it, the last first.
internal sealed record DeadJobRecord
{
    public required Guid Id { get; init; }

    public required string Type { get; init; }

    public required string Queue { get; init; }

    public int Attempts { get; init; }

    public string? LastError { get; init; }

    public DateTimeOffset? DeadLetteredAt { get; init; }
}

// A delivery of an event to an outbox handler that used all its attempts, as GET /events/dead-letter lists it, the last
// first. Its payload is never shown.
internal sealed record FailedDeliveryRecord
{
    public required Guid Id { get; init; }

    public required Guid EventId { get; init; }

    public required string EventType { get; init; }

    public required string Handler { get; init; }

    public int Attempts { get; init; }

    public string? LastError { get; init; }

    public DateTimeOffset OccurredAt { get; init; }
}

internal sealed record PurgedRecord
{
    public required string Handler { get; init; }

    public int Removed { get; init; }
}

// The calls of the jobs area (spec 4.15). Every one needs jobs.manage for the instance, and none is about content, so none
// sends a content locale. The two lists of dead letters have no total: a page that comes back full may have one after it.
internal static class JobsApi
{
    // The filters of the list, by the names of their query parameters, which the list's address and the API share.
    public const string StatusFilter = "status";
    public const string TypeFilter = "type";
    public const string QueueFilter = "queue";

    public const int DeadLetterPageSize = 25;

    public static readonly IReadOnlyList<string> Statuses = ["pending", "running", "succeeded", "dead_lettered"];

    public static async Task<RecordPage<JobRecord>> GetJobsAsync(this IManagementApi api, RecordQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(query);
        var path = $"jobs?page={Number(query.Page)}&pageSize={Number(query.PageSize)}";
        foreach (var name in (string[])[StatusFilter, TypeFilter, QueueFilter])
        {
            if (query.Filter(name) is { } value)
            {
                path += $"&{name}={Uri.EscapeDataString(value)}";
            }
        }

        var page = (await api.SendAsync(Request(HttpMethod.Get, path), JobsJsonContext.Default.JobRecordPage, cancellationToken)).Value;
        return new RecordPage<JobRecord>(page.Items, page.Total);
    }

    public static Task<JobDetailRecord> GetJobAsync(this IManagementApi api, Guid id, CancellationToken cancellationToken) =>
        api.JobAsync(Request(HttpMethod.Get, $"jobs/{id:D}"), cancellationToken);

    // Only a dead-lettered job is retried, with as many attempts again as a new job gets.
    public static Task<JobDetailRecord> RetryJobAsync(this IManagementApi api, Guid id, CancellationToken cancellationToken) =>
        api.JobAsync(Request(HttpMethod.Post, $"jobs/{id:D}/retry"), cancellationToken);

    // Only a pending job is cancelled, which puts it among the dead letters; one that runs is left to finish.
    public static Task<JobDetailRecord> CancelJobAsync(this IManagementApi api, Guid id, CancellationToken cancellationToken) =>
        api.JobAsync(Request(HttpMethod.Post, $"jobs/{id:D}/cancel"), cancellationToken);

    public static async Task<IReadOnlyList<DeadJobRecord>> GetDeadJobsAsync(this IManagementApi api, int page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(api);
        var request = Request(HttpMethod.Get, $"jobs/dead-letter?page={Number(page)}&pageSize={Number(DeadLetterPageSize)}");
        return (await api.SendAsync(request, JobsJsonContext.Default.ItemListDeadJobRecord, cancellationToken)).Value.Items;
    }

    public static async Task<IReadOnlyList<FailedDeliveryRecord>> GetFailedDeliveriesAsync(this IManagementApi api, int page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(api);
        var request = Request(HttpMethod.Get, $"events/dead-letter?page={Number(page)}&pageSize={Number(DeadLetterPageSize)}");
        return (await api.SendAsync(request, JobsJsonContext.Default.ItemListFailedDeliveryRecord, cancellationToken)).Value.Items;
    }

    public static Task RetryDeliveryAsync(this IManagementApi api, Guid id, CancellationToken cancellationToken) =>
        api.SendAsync(Request(HttpMethod.Post, $"events/dead-letter/{id:D}/retry"), cancellationToken);

    public static Task RemoveDeliveryAsync(this IManagementApi api, Guid id, CancellationToken cancellationToken) =>
        api.SendAsync(Request(HttpMethod.Delete, $"events/dead-letter/{id:D}"), cancellationToken);

    // Every failed delivery of the handler, those not read too.
    public static async Task<PurgedRecord> PurgeDeliveriesAsync(this IManagementApi api, string handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(api);
        var request = Request(HttpMethod.Delete, $"events/dead-letter?handler={Uri.EscapeDataString(handler)}");
        return (await api.SendAsync(request, JobsJsonContext.Default.PurgedRecord, cancellationToken)).Value;
    }

    private static async Task<JobDetailRecord> JobAsync(this IManagementApi api, ApiRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(api);
        return (await api.SendAsync(request, JobsJsonContext.Default.JobDetailRecord, cancellationToken)).Value;
    }

    private static ApiRequest Request(HttpMethod method, string path) => new(method, path) { NoLocale = true };

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}

// The answers of the routes the area tells apart, by status, code and path only (spec 2.7).
internal static class JobProblems
{
    public const string JobNotFound = "FCMS3132";
    public const string NotDeadLettered = "FCMS3133";
    public const string NotCancellable = "FCMS3134";
    public const string DeliveryNotFound = "FCMS3138";
    public const string DeliveryNotFailed = "FCMS3139";

    // The error of an attempt that ran past its timeout, which the runner abandoned when it ignored its token.
    public const string AttemptTimedOut = "FCMS2508";

    public static bool IsFailure(Exception exception) =>
        exception is ApiProblemException or HttpRequestException or TaskCanceledException or JsonException;

    public static bool Has(Exception exception, string code) =>
        exception is ApiProblemException { Problem: var problem } && (problem.Code == code || problem.Errors.Any(error => error.Code == code));
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(JobRecordPage))]
[JsonSerializable(typeof(JobDetailRecord))]
[JsonSerializable(typeof(ItemList<DeadJobRecord>))]
[JsonSerializable(typeof(ItemList<FailedDeliveryRecord>))]
[JsonSerializable(typeof(PurgedRecord))]
internal sealed partial class JobsJsonContext : JsonSerializerContext;
