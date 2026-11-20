using System.Net;
using FluentCMS.Admin.Actions;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Localization;
using Microsoft.Extensions.Localization;

namespace FluentCMS.Admin.Content;

// Sends the list's own bulk actions (spec 4.13). A bulk call answers only when it ends, so the ids go in chunks, one after
// another, each with a key of its own that a resend of it carries again, and each answer is reported as it comes. A chunk
// that fails as a whole, unanswered or with a 5xx, a 429 or a key still held, is sent again a few times, then the user
// chooses to try again, with the same key, or to stop; the user's cancel stops before the next chunk and cuts a wait short,
// never a request in flight.
internal sealed class BulkRunner(ManagementClient client, TimeProvider time, IStringLocalizer<AdminResources> localizer)
{
    public const int ChunkSize = 50;

    // The most ids a selection of all that match is read for, the server's limit of one bulk request.
    public const int MaxEntries = 1000;

    public const int MaxAttempts = 3;

    // Doubled for each attempt after the second, unless the answer's Retry-After asks for longer; the pipeline has already
    // waited a 429's Retry-After once.
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    // A wait longer than this is not waited out: the user is asked instead.
    public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    private const string KeyInProgress = "FCMS3142";

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

            var goesOn = await SendAsync(type, operation, chunk, new UserAction(ApiRequest.NewIdempotencyKey(), site, locale), progress, sent);
            sent += chunk.Length;
            progress.ChunkDone();
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

    // Whether the run goes on after the chunk.
    private async Task<bool> SendAsync(string type, BulkOperation operation, Guid[] chunk, UserAction action, BulkProgress progress, int sent)
    {
        while (true)
        {
            var outcome = await AttemptsAsync(type, operation, chunk, action, progress.Cancelled);
            if (outcome.Answer is { } answer)
            {
                foreach (var item in answer.Items)
                {
                    progress.Report(Result(operation, item, chunk));
                }

                return !answer.Cancelled;
            }

            var reason = outcome.Problem?.Detail ?? outcome.Problem?.Title ?? localizer["Content.Bulk.Unreachable"];
            var range = localizer["Content.Bulk.ChunkOfIds", sent + 1, sent + chunk.Length].Value;
            if (!outcome.Final && await progress.AskToContinueAsync(localizer["Content.Bulk.ChunkFailed", range, reason]))
            {
                continue;
            }

            // A chunk that went unanswered may have committed, so its entries are not called failed; one refused before it
            // ran, by a 429 or a refusal of the whole request, is failed or did not run.
            var status = outcome.Unconfirmed ? BulkStatuses.NotConfirmed : outcome.Final ? BulkStatuses.Failed : BulkStatuses.NotRun;
            var failure = outcome.Problem ?? new ApiProblem { Title = reason };
            foreach (var id in chunk)
            {
                progress.Report(new BulkItemResult { Id = id, Status = status, Problem = status == BulkStatuses.NotRun ? null : failure });
            }

            progress.Fail(localizer["Content.Bulk.RunStopped", range, reason]);
            return false;
        }
    }

    private sealed record Outcome(BulkAnswer? Answer, ApiProblem? Problem, bool Final, bool Unconfirmed);

    // The answer, or the problem of the last attempt, whether sending the same chunk again could answer otherwise, and whether
    // the server may have run it: no answer, a 5xx or a key still held, as against a 429, refused before the endpoint ran.
    private async Task<Outcome> AttemptsAsync(string type, BulkOperation operation, Guid[] chunk, UserAction action, CancellationToken cancelled)
    {
        ApiProblem? problem = null;
        var unconfirmed = false;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            if (attempt > 1)
            {
                var backoff = RetryDelay * (attempt - 1);
                var wait = problem?.RetryAfter is { } asked && asked > backoff ? asked : backoff;
                if (wait > MaxRetryDelay)
                {
                    break;
                }

                try
                {
                    await Task.Delay(wait, time, cancelled);
                }
                catch (OperationCanceledException) when (cancelled.IsCancellationRequested)
                {
                    break;
                }
            }

            try
            {
                return new(await client.BulkAsync(type, operation, chunk, action, CancellationToken.None), null, false, false);
            }
            catch (ApiProblemException failure) when (Retryable(failure.Problem))
            {
                problem = failure.Problem;
                unconfirmed |= failure.Problem.Status != (int)HttpStatusCode.TooManyRequests;
            }
            catch (ApiProblemException failure)
            {
                // A refusal after an attempt that went unanswered may still hide that attempt's commit.
                return new(null, failure.Problem, true, unconfirmed);
            }
            catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                problem = null;
                unconfirmed = true;
            }
        }

        return new(null, problem, false, unconfirmed);
    }

    private static bool Retryable(ApiProblem problem) =>
        problem.Status is (int)HttpStatusCode.TooManyRequests or >= 500 || (problem.Status == (int)HttpStatusCode.Conflict && problem.Code == KeyInProgress);

    private static BulkItemResult Result(BulkOperation operation, BulkAnswerItem item, Guid[] chunk)
    {
        var id = item.Id ?? (item.Index >= 0 && item.Index < chunk.Length ? chunk[item.Index] : Guid.Empty);
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
