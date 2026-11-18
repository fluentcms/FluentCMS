using FluentCMS.Admin.Api;
using FluentCMS.Plugins.Media.Admin.Api;
using Microsoft.AspNetCore.Components.Forms;

namespace FluentCMS.Plugins.Media.Admin;

internal enum MediaUploadState
{
    Waiting,
    Uploading,
    Done,
    Refused,
}

// One file of an upload, with its state and, once refused, the code and text of its refusal.
internal sealed class MediaUploadItem(IBrowserFile file, Guid? folder)
{
    public string FileName { get; } = file.Name;

    // The folder that was open when the file was added.
    public Guid? Folder { get; } = folder;

    public MediaUploadState State { get; set; }

    public string? Code { get; set; }

    public string? Detail { get; set; }

    // The browser's file until it is read, so that its bytes are held only while it is sent.
    public IBrowserFile? File { get; set; } = file;
}

// Uploads files one request each, one after another, so that each has a state of its own and a refused file stops no
// other (spec 4.14). The state is per file and not per byte: the browser's fetch reports no progress of an upload. A file
// is read right before it is sent, so that only that one is held in memory; the file input forgets the files of a choice
// once the next one is made, so the caller takes no other choice while HasUnread is true.
internal sealed class MediaUploadQueue(MediaApi api)
{
    // The default limit of a request of the host (FluentCms:Media:MaxRequestBodySize); a larger file is refused unread.
    public const long MaxFileBytes = 256L * 1024 * 1024;

    public const string TooLarge = "FCMS1582";

    private readonly List<MediaUploadItem> _items = [];

    private bool _running;

    public IReadOnlyList<MediaUploadItem> Items => _items;

    public bool Running => _running;

    // Files of a choice wait to be read, which a new choice would make the browser forget.
    public bool HasUnread => _items.Any(item => item.File is not null);

    public void Clear() => _items.RemoveAll(item => item.State is MediaUploadState.Done or MediaUploadState.Refused);

    // Adds the files and uploads every waiting one, each into the folder it was added to; changed runs after each state.
    // False when the uploads that were already running take the files.
    public async Task<bool> AddAsync(IReadOnlyList<IBrowserFile> files, Guid? folder, Func<Task> changed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(changed);
        foreach (var file in files)
        {
            var item = new MediaUploadItem(file, folder);
            if (file.Size > MaxFileBytes)
            {
                (item.State, item.Code, item.File) = (MediaUploadState.Refused, TooLarge, null);
            }

            _items.Add(item);
        }

        await changed();
        if (_running)
        {
            return false;
        }

        _running = true;
        try
        {
            while (_items.FirstOrDefault(item => item.State == MediaUploadState.Waiting) is { } next)
            {
                next.State = MediaUploadState.Uploading;
                await changed();
                await SendAsync(next, cancellationToken);
                await changed();
            }
        }
        finally
        {
            _running = false;
        }

        return true;
    }

    private async Task SendAsync(MediaUploadItem item, CancellationToken cancellationToken)
    {
        var file = item.File!;
        item.File = null;
        MediaUploadBody body;
        try
        {
            body = MediaUploadBody.Create(item.Folder, item.FileName, file.Size);
            await using var stream = file.OpenReadStream(MaxFileBytes, cancellationToken);
            await stream.ReadExactlyAsync(body.File, cancellationToken);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // The browser could not read the file, as one deleted since it was chosen; its message is not in the user's language.
            item.State = MediaUploadState.Refused;
            return;
        }

        try
        {
            await api.UploadAsync(body, cancellationToken);
            item.State = MediaUploadState.Done;
        }
        catch (ApiProblemException failure)
        {
            (item.State, item.Code, item.Detail) = (MediaUploadState.Refused, failure.Problem.Code, failure.Problem.Detail ?? failure.Problem.Title);
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            item.State = MediaUploadState.Refused;
        }
    }
}
