using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Administration.Jobs;

// The jobs area against a fake of /jobs and /events/dead-letter that keeps what the pages change, as the server does: a
// retry makes a dead-lettered job pending again, a cancel dead-letters a pending one, and a delivery retried, removed or
// purged leaves the list. A refusal is given with Management.Answer, which is asked first. The clock of the page is Time,
// whose waits end only when the test ticks.
public abstract class JobsTestContext : AdministrationTestContext
{
    internal static readonly Guid Publish = new("0193a1b2-0000-7000-8000-0000000000b1");
    internal static readonly Guid Rebuild = new("0193a1b2-0000-7000-8000-0000000000b2");
    internal static readonly Guid Mail = new("0193a1b2-0000-7000-8000-0000000000b3");
    internal static readonly Guid Cleanup = new("0193a1b2-0000-7000-8000-0000000000b4");
    internal static readonly Guid Published = new("0193a1b2-0000-7000-8000-0000000000c1");
    internal static readonly Guid Trashed = new("0193a1b2-0000-7000-8000-0000000000c2");
    internal static readonly Guid Purged = new("0193a1b2-0000-7000-8000-0000000000c3");
    internal static readonly DateTimeOffset Now = new(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);
    internal const string TimedOut = "FCMS2508: the attempt ran longer than 00:30:00.";

    // The error of an attempt abandoned past its timeout as the pages show it.
    internal const string TimedOutShown = "It ran past its timeout and was abandoned (FCMS2508): the attempt ran longer than 00:30:00.";

    protected JobsTestContext()
    {
        Services.AddSingleton<TimeProvider>(Time);
        Jobs.Add(Job(Publish, "content.publish", "default", "running", attempts: 1));
        Jobs.Add(Job(Rebuild, "search.rebuild", "default", "dead_lettered", attempts: 2, error: TimedOut, completed: Now.AddMinutes(-5)));
        Jobs.Add(Job(Mail, "mail.send", "mail", "pending", runAt: Now.AddHours(1)));
        Jobs.Add(Job(Cleanup, "media.cleanup", "default", "succeeded", attempts: 1, completed: Now.AddHours(-1)));
        History[Rebuild] =
        [
            Attempt(1, "failed", TimedOut),
            Attempt(2, "lease_expired", "The worker stopped without finishing this attempt, and its lease expired."),
        ];
        History[Publish] = [Attempt(1, null, null)];
        Deliveries.Add(Delivery(Published, "EntryPublishedEvent", "search.content.published", "The index is locked."));
        Deliveries.Add(Delivery(Trashed, "EntryTrashedEvent", "search.content.trashed", "The index is locked."));
        Deliveries.Add(Delivery(Purged, "EntryPurgedEvent", "search.content.purged", "The index is gone."));
        Management.Respond = Answer;
    }

    internal TickingTime Time { get; } = new(Now);

    internal JsonArray Jobs { get; } = [];

    internal Dictionary<Guid, JsonArray> History { get; } = [];

    internal JsonArray Deliveries { get; } = [];

    // The query strings of the reads of the list.
    internal List<string> JobQueries { get; } = [];

    // The reads of one job, by its id.
    internal int JobReads(Guid id) => Management.Calls.Count(call => call.Method == HttpMethod.Get && call.Path == $"jobs/{id:D}");

    internal static JsonObject Job(Guid id, string type, string queue, string status, int attempts = 0, string? error = null, DateTimeOffset? runAt = null, DateTimeOffset? completed = null) => new()
    {
        ["id"] = id,
        ["type"] = type,
        ["queue"] = queue,
        ["siteId"] = null,
        ["status"] = status,
        ["attempts"] = attempts,
        ["maxAttempts"] = 5,
        ["priority"] = 0,
        ["runAt"] = runAt ?? Now.AddMinutes(-10),
        ["lastError"] = error,
        ["createdAt"] = Now.AddMinutes(-10),
        ["createdBy"] = null,
        ["origin"] = "api",
        ["completedAt"] = completed,
    };

    internal static JsonObject Attempt(int attempt, string? outcome, string? error) => new()
    {
        ["attempt"] = attempt,
        ["startedAt"] = Now.AddMinutes(-9 + attempt),
        ["finishedAt"] = outcome is null ? null : Now.AddMinutes(-8 + attempt),
        ["outcome"] = outcome,
        ["error"] = error,
    };

    internal static JsonObject Delivery(Guid id, string eventType, string handler, string error) => new()
    {
        ["id"] = id,
        ["eventId"] = Guid.NewGuid(),
        ["eventType"] = eventType,
        ["handler"] = handler,
        ["attempts"] = 10,
        ["lastError"] = error,
        ["occurredAt"] = Now.AddHours(-2),
    };

    internal JsonObject JobOf(Guid id) => Jobs.Single(job => (Guid)job!["id"]! == id)!.AsObject();

    internal static IElement Section(IRenderedComponent<AdminApp> app, string heading) =>
        app.FindAll("section.cms-card").Single(section => section.QuerySelector("h2")!.TextContent == heading);

    // A button by its accessible text: the visible text and the hidden part.
    internal static IElement Button(IElement scope, string name) =>
        scope.QuerySelectorAll("fluent-button").Single(button => button.TextContent.Trim() == name);

    internal static bool HasButton(IElement scope, string name) =>
        scope.QuerySelectorAll("fluent-button").Any(button => button.TextContent.Trim() == name);

    internal static string Message(IElement scope) => scope.QuerySelector(".jobs-message")!.TextContent;

    // Each row of a table of the scope, its cells' texts.
    internal static string[][] Rows(IElement scope) =>
        [.. scope.QuerySelectorAll("tbody tr").Select(row => row.Children.Select(cell => cell.TextContent.Trim()).ToArray())];

    // A state that changes without a render, such as a wait the page starts, which WaitForAssertion would not see again.
    internal static void Eventually(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            DateTime.UtcNow.ShouldBeLessThan(until, "The condition did not hold in time.");
            Thread.Sleep(10);
        }
    }

    private static string? Value(string query, string name) =>
        query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(pair => pair.Split('=', 2))
            .LastOrDefault(pair => pair[0] == name) is [_, var value] ? Uri.UnescapeDataString(value) : null;

    private static JsonObject Page(IEnumerable<JsonNode> rows, string query)
    {
        var (page, size) = (int.Parse(Value(query, "page") ?? "1", CultureInfo.InvariantCulture), int.Parse(Value(query, "pageSize") ?? "25", CultureInfo.InvariantCulture));
        var all = rows.ToList();
        return new JsonObject
        {
            ["items"] = new JsonArray([.. all.Skip((page - 1) * size).Take(size).Select(row => row.DeepClone())]),
            ["total"] = all.Count,
            ["page"] = page,
            ["pageSize"] = size,
        };
    }

    private JsonObject Detail(Guid id)
    {
        var detail = JobOf(id).DeepClone().AsObject();
        detail["history"] = History.TryGetValue(id, out var attempts) ? attempts.DeepClone() : new JsonArray();
        return detail;
    }

    private HttpResponseMessage? Answer(HttpRequestMessage request, string path, string? body)
    {
        var query = request.RequestUri!.Query;
        var method = request.Method.Method;
        var segments = path.Split('/');
        var known = Jobs.Select(job => (Guid)job!["id"]!).ToHashSet();
        switch (method, segments)
        {
            case ("GET", ["jobs"]):
                JobQueries.Add(query.TrimStart('?'));
                return Json(Page(Jobs.Where(job =>
                    (Value(query, "status") is not { } status || (string)job!["status"]! == status)
                    && (Value(query, "type") is not { } type || (string)job!["type"]! == type)
                    && (Value(query, "queue") is not { } queue || (string)job!["queue"]! == queue))!, query));
            case ("GET", ["jobs", "dead-letter"]):
                return Json(new JsonObject
                {
                    ["items"] = Page(Jobs.Where(job => (string)job!["status"]! == "dead_lettered").Select(job => (JsonNode)new JsonObject
                    {
                        ["id"] = job!["id"]!.DeepClone(),
                        ["type"] = job["type"]!.DeepClone(),
                        ["queue"] = job["queue"]!.DeepClone(),
                        ["attempts"] = job["attempts"]!.DeepClone(),
                        ["lastError"] = job["lastError"]?.DeepClone(),
                        ["deadLetteredAt"] = job["completedAt"]?.DeepClone(),
                    }), query)["items"]!.DeepClone(),
                });
            case (_, ["jobs", var id, ..]) when !known.Contains(Guid.Parse(id)):
                return Problem(HttpStatusCode.NotFound, "FCMS3132", "id");
            case ("GET", ["jobs", var id]):
                return Json(Detail(Guid.Parse(id)));
            case ("POST", ["jobs", var id, "retry"]):
                var dead = JobOf(Guid.Parse(id));
                if ((string)dead["status"]! != "dead_lettered")
                {
                    return Problem(HttpStatusCode.Conflict, "FCMS3133", "id");
                }

                (dead["status"], dead["runAt"], dead["completedAt"], dead["maxAttempts"]) = ("pending", Now, null, (int)dead["attempts"]! + 5);
                return Json(Detail(Guid.Parse(id)));
            case ("POST", ["jobs", var id, "cancel"]):
                var pending = JobOf(Guid.Parse(id));
                if ((string)pending["status"]! != "pending")
                {
                    return Problem(HttpStatusCode.Conflict, "FCMS3134", "id");
                }

                (pending["status"], pending["completedAt"], pending["lastError"]) = ("dead_lettered", Now, "The job was cancelled before it ran.");
                return Json(Detail(Guid.Parse(id)));
            case ("GET", ["events", "dead-letter"]):
                return Json(new JsonObject { ["items"] = Page(Deliveries!, query)["items"]!.DeepClone() });
            case ("DELETE", ["events", "dead-letter"]):
                var handler = Value(query, "handler")!;
                var removed = Deliveries.Where(delivery => (string)delivery!["handler"]! == handler).ToList();
                removed.ForEach(delivery => Deliveries.Remove(delivery));
                return Json(new JsonObject { ["handler"] = handler, ["removed"] = removed.Count + 4 });
            case ("POST", ["events", "dead-letter", _, "retry"]) or ("DELETE", ["events", "dead-letter", _]):
                if (Deliveries.FirstOrDefault(delivery => (Guid)delivery!["id"]! == Guid.Parse(segments[2])) is not { } failed)
                {
                    return Problem(HttpStatusCode.NotFound, "FCMS3138", "id");
                }

                Deliveries.Remove(failed);
                return NoContent();
            default:
                return null;
        }
    }

    // A clock whose waits end only when the test ticks: each wait of the pages is a timer the test sees.
    internal sealed class TickingTime(DateTimeOffset now) : TimeProvider
    {
        private readonly Lock _gate = new();
        private readonly List<Waiting> _waits = [];

        public DateTimeOffset Now { get; set; } = now;

        // The waits that have not ended or been given up, with how long each is.
        public IReadOnlyList<TimeSpan> Waits
        {
            get
            {
                lock (_gate)
                {
                    return [.. _waits.Where(wait => !wait.Disposed).Select(wait => wait.Due)];
                }
            }
        }

        public override DateTimeOffset GetUtcNow() => Now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var wait = new Waiting(callback, state, dueTime);
            lock (_gate)
            {
                _waits.Add(wait);
            }

            return wait;
        }

        // Ends every wait that is under way.
        public void Tick()
        {
            List<Waiting> due;
            lock (_gate)
            {
                due = [.. _waits.Where(wait => !wait.Disposed)];
                _waits.Clear();
            }

            foreach (var wait in due)
            {
                wait.Fire();
            }
        }

        private sealed class Waiting(TimerCallback callback, object? state, TimeSpan due) : ITimer
        {
            public TimeSpan Due => due;

            public bool Disposed { get; private set; }

            public void Fire() => callback(state);

            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose() => Disposed = true;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
