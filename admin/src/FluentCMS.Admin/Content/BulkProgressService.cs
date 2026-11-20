using FluentCMS.Admin.Actions;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace FluentCMS.Admin.Content;

// The bulk progress service of the contract (spec 4.13, 5.1): each operation shows a dialog over the page with a progress
// bar and the result of each item, and the list's own actions report to it as a half's do.
internal sealed class BulkProgressService(IAdminOverlays overlays, IAnnouncer announcer, IStringLocalizer<AdminResources> localizer, TimeProvider time) : IBulkProgressService
{
    public IBulkProgress Start(string title, int total) => Begin(title, total);

    // A chunked run announces once per chunk it ends; any other, a half's, once per ChunkSize results.
    public BulkProgress Begin(string title, int total, IReadOnlyDictionary<Guid, string>? labels = null, bool chunked = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentOutOfRangeException.ThrowIfNegative(total);
        var progress = new BulkProgress(title, total, labels ?? new Dictionary<Guid, string>(), chunked, announcer, localizer, time);
        progress.Show(overlays.Show(builder =>
        {
            builder.OpenComponent<BulkProgressDialog>(0);
            builder.AddComponentParameter(1, nameof(BulkProgressDialog.State), progress);
            builder.CloseComponent();
        }));
        return progress;
    }
}

// One operation's progress. Progress is announced once per chunk of results, not per item, and once at the end, so that a
// run of hundreds of entries does not flood the live region (WCAG 4.1.3), which keeps the last five. Disposing it ends the
// operation; the dialog stays until the user closes it, which Closed then tells. A half's operation that has not ended within
// StopGrace of a cancel, one that ignores its token, is ended here, so the dialog can always be closed; what it reports or
// fails later is dropped. The list's own chunked run is never ended so: it waits for the chunk in flight, which answers or
// times out within the pipeline's attempt timeout, so that each of its entries gets its result.
internal sealed class BulkProgress : IBulkProgress
{
    public static readonly TimeSpan StopGrace = TimeSpan.FromSeconds(10);

    private readonly CancellationTokenSource _cancel = new();
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<BulkItemResult> _results = [];
    private readonly IAnnouncer _announcer;
    private readonly IStringLocalizer<AdminResources> _localizer;
    private readonly TimeProvider _time;
    private readonly bool _chunked;
    private ITimer? _grace;
    private IDisposable? _overlay;
    private TaskCompletionSource<bool>? _question;

    internal BulkProgress(string title, int total, IReadOnlyDictionary<Guid, string> labels, bool chunked, IAnnouncer announcer, IStringLocalizer<AdminResources> localizer, TimeProvider time)
    {
        (Title, Total, Labels, _chunked, _announcer, _localizer, _time) = (title, total, labels, chunked, announcer, localizer, time);
        Cancelled = _cancel.Token;
    }

    public event Action? Changed;

    public string Title { get; }

    public int Total { get; }

    // The labels of the entries the list knows, by id; an item without one shows its id.
    public IReadOnlyDictionary<Guid, string> Labels { get; }

    public IReadOnlyList<BulkItemResult> Results => _results;

    public int Failed => _results.Count(result => result.Problem is not null && result.Status != BulkStatuses.NotConfirmed);

    // Sent without an answer, so they may or may not have been done.
    public int Unconfirmed => _results.Count(result => result.Status == BulkStatuses.NotConfirmed);

    // The items that were sent or ran, whatever their result; those reported as not run do not count.
    public int Processed { get; private set; }

    public bool Ended { get; private set; }

    public bool Stopping => _cancel.IsCancellationRequested;

    // What stopped the operation as a whole, such as a chunk that could not be sent, or null.
    public string? Problem { get; private set; }

    // The question asked after a chunk failed as a whole, while it waits for the user.
    public string? Asking { get; private set; }

    public CancellationToken Cancelled { get; }

    public Task Closed => _closed.Task;

    public void Report(BulkItemResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (Ended)
        {
            return;
        }

        _results.Add(result);
        if (result.Status != BulkStatuses.NotRun)
        {
            Processed++;
            if (!_chunked && Processed % BulkRunner.ChunkSize == 0)
            {
                AnnounceProgress();
            }
        }

        Changed?.Invoke();
    }

    // The end of a chunk of a chunked run.
    public void ChunkDone() => AnnounceProgress();

    public void Cancel()
    {
        if (Ended)
        {
            return;
        }

        _cancel.Cancel();
        _question?.TrySetResult(false);
        if (!_chunked)
        {
            _grace ??= _time.CreateTimer(_ => Abandon(), null, StopGrace, Timeout.InfiniteTimeSpan);
        }

        Changed?.Invoke();
    }

    public void Fail(string problem)
    {
        if (Ended)
        {
            return;
        }

        Problem = problem;
        _cancel.Cancel();
        Changed?.Invoke();
    }

    // Whether to send a chunk that failed as a whole again; false once the user stops, or cancels the run.
    public Task<bool> AskToContinueAsync(string question)
    {
        if (Stopping || Ended)
        {
            return Task.FromResult(false);
        }

        Asking = question;
        _question = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Changed?.Invoke();
        return _question.Task;
    }

    public void Answer(bool again)
    {
        if (Ended)
        {
            return;
        }

        Asking = null;
        if (!again)
        {
            _cancel.Cancel();
        }

        _question?.TrySetResult(again);
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (Ended)
        {
            return;
        }

        Ended = true;
        _grace?.Dispose();
        Asking = null;
        _question?.TrySetResult(false);
        _announcer.Announce(Summary());
        Changed?.Invoke();
    }

    public string Summary()
    {
        var done = Processed - Failed - Unconfirmed;
        string summary = Stopping && Processed < Total
            ? _localizer["Content.Bulk.Stopped", done, Total, Failed]
            : _localizer["Content.Bulk.Ended", done, Failed];
        return Unconfirmed > 0 ? $"{summary} {_localizer["Content.Bulk.Unconfirmed", Unconfirmed]}" : summary;
    }

    internal void Show(IDisposable overlay) => _overlay = overlay;

    // The dialog was closed after the end: the overlay goes, and whoever waits on Closed goes on.
    internal void Close()
    {
        _overlay?.Dispose();
        _overlay = null;
        _grace?.Dispose();
        _cancel.Dispose();
        _closed.TrySetResult();
    }

    private void AnnounceProgress()
    {
        if (!Ended && Processed < Total)
        {
            _announcer.Announce(_localizer["Content.Bulk.Progress", Processed, Total, Failed]);
        }
    }

    // The operation did not stop within the grace after a cancel: it is ended as it stands, and what it reports later is dropped.
    private void Abandon()
    {
        if (!Ended)
        {
            Problem = _localizer["Content.Bulk.DidNotStop", StopGrace.TotalSeconds];
            Dispose();
        }
    }
}

// The statuses of an item the admin writes, besides a half's own.
internal static class BulkStatuses
{
    public const string Published = "published";
    public const string Unpublished = "unpublished";
    public const string Deleted = "deleted";
    public const string Failed = "failed";
    public const string Refused = "refused";
    public const string Duplicate = "duplicate";
    public const string NotRun = "notRun";
    public const string NotConfirmed = "notConfirmed";

    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal) { Published, Unpublished, Deleted, Failed, Refused, Duplicate, NotRun, NotConfirmed };
}
