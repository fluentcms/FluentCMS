using System.Net;
using FluentCMS.Admin.Actions;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Lists;
using FluentCMS.Admin.Localization;
using Microsoft.Extensions.Localization;

namespace FluentCMS.Admin.Content;

// Sends the list's own bulk actions (spec 4.13). A bulk call answers only when it ends, so a selection by id goes in chunks,
// one after another, each with a key of its own that a retry of it sends again, and each answer is reported as it comes. A
// chunk that fails as a whole, unanswered or with a 5xx, a 429 or a key still held, is sent again a few times, then the user
// chooses to try again or to stop; the user's cancel stops before the next chunk, never inside one.
internal sealed class BulkRunner(ManagementClient client, TimeProvider time, IStringLocalizer<AdminResources> localizer)
{
    public const int ChunkSize = 50;

    // The most a selection by filter sends as its cap, the server's limit of a bulk request.
    public const int MaxCap = 1000;

    public const int MaxAttempts = 3;

    // Doubled for each attempt after the second; the pipeline has already waited a 429's Retry-After once.
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private const string KeyInProgress = "FCMS3142";

    // The server takes a selection by filter only with a filter, so a list without one sends one every entry matches.
    private static readonly string Everything = new ContentQuery { Filter = new FieldFilter("id", FilterOperator.NotEqual, Guid.Empty) }.ToQueryString();

    public async Task RunAsync(string type, BulkOperation operation, IReadOnlyList<Guid> ids, string? site, string? locale, BulkProgress progress)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(progress);
        var sent = 0;
        foreach (var chunk in ids.Chunk(ChunkSize))
        {
            if (progress.Stopping)
            {
                break;
            }

            var action = new UserAction(ApiRequest.NewIdempotencyKey(), site, locale);
            var goesOn = await SendAsync(type, operation, chunk, null, null, action, progress, sent);
            sent += chunk.Length;
            if (!goesOn)
            {
                break;
            }
        }

        foreach (var id in ids.Skip(sent))
        {
            progress.Report(new BulkItemResult { Id = id, Status = BulkStatuses.NotRun });
        }
    }

    // A selection by filter: one call, whose results come at its end.
    public Task RunAsync(string type, BulkOperation operation, string? filterQuery, int cap, string? site, string? locale, BulkProgress progress) =>
        SendAsync(type, operation, null, string.IsNullOrEmpty(filterQuery) ? Everything : filterQuery, Math.Clamp(cap, 1, MaxCap), new UserAction(ApiRequest.NewIdempotencyKey(), site, locale), progress, 0);

    // Whether the run goes on after the chunk.
    private async Task<bool> SendAsync(string type, BulkOperation operation, Guid[]? chunk, string? filter, int? cap, UserAction action, BulkProgress progress, int sent)
    {
        while (true)
        {
            var (answer, problem, final) = await AttemptsAsync(type, operation, chunk, filter, cap, action);
            if (answer is not null)
            {
                foreach (var item in answer.Items)
                {
                    progress.Report(Result(operation, item, chunk));
                }

                return !answer.Cancelled;
            }

            var reason = problem?.Detail ?? problem?.Title ?? localizer["Content.Bulk.Unreachable"];
            var range = chunk is null ? localizer["Content.Bulk.ChunkOfFilter"].Value : localizer["Content.Bulk.ChunkOfIds", sent + 1, sent + chunk.Length].Value;
            if (!final && await progress.AskToContinueAsync(localizer["Content.Bulk.ChunkFailed", range, reason]))
            {
                continue;
            }

            // The chunk's entries are reported with the problem that stopped it; a selection by filter has no ids to report.
            var failure = problem ?? new ApiProblem { Title = reason };
            foreach (var id in chunk ?? [])
            {
                progress.Report(new BulkItemResult { Id = id, Status = BulkStatuses.Failed, Problem = failure });
            }

            progress.Fail(localizer["Content.Bulk.RunStopped", range, reason]);
            return false;
        }
    }

    // The answer, or the problem of the last attempt and whether sending the same chunk again could answer otherwise.
    private async Task<(BulkAnswer? Answer, ApiProblem? Problem, bool Final)> AttemptsAsync(string type, BulkOperation operation, Guid[]? chunk, string? filter, int? cap, UserAction action)
    {
        ApiProblem? problem = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            if (attempt > 1)
            {
                await Task.Delay(RetryDelay * (attempt - 1), time);
            }

            try
            {
                return (await client.BulkAsync(type, operation, chunk, filter, cap, action, CancellationToken.None), null, false);
            }
            catch (ApiProblemException failure) when (Retryable(failure.Problem))
            {
                problem = failure.Problem;
            }
            catch (ApiProblemException failure)
            {
                return (null, failure.Problem, true);
            }
            catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                problem = null;
            }
        }

        return (null, problem, false);
    }

    private static bool Retryable(ApiProblem problem) =>
        problem.Status is (int)HttpStatusCode.TooManyRequests or >= 500 || (problem.Status == (int)HttpStatusCode.Conflict && problem.Code == KeyInProgress);

    private static BulkItemResult Result(BulkOperation operation, BulkAnswerItem item, Guid[]? chunk)
    {
        var id = item.Id ?? (chunk is not null && item.Index >= 0 && item.Index < chunk.Length ? chunk[item.Index] : Guid.Empty);
        var status = item.Status switch
        {
            "succeeded" => operation switch
            {
                BulkOperation.Publish => BulkStatuses.Published,
                BulkOperation.Unpublish => BulkStatuses.Unpublished,
                _ => BulkStatuses.Deleted,
            },
            "failed" when item.Problem?.Status == (int)HttpStatusCode.Forbidden => BulkStatuses.Refused,
            "failed" => BulkStatuses.Failed,
            "duplicate" => BulkStatuses.Duplicate,
            _ => BulkStatuses.NotRun,
        };
        return new BulkItemResult
        {
            Id = id,
            Status = status,
            Warnings = [.. item.Warnings.Select(warning => warning.Message)],
            Problem = item.Status == "failed" ? item.Problem ?? new ApiProblem() : null,
        };
    }
}
