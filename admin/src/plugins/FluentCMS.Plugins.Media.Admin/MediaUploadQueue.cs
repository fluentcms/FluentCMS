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
internal sealed class MediaUploadItem(string fileName, Guid? folder)
{
    public string FileName { get; } = fileName;

    // The folder that was open when the file was added.
    public Guid? Folder { get; } = folder;

    public MediaUploadState State { get; set; }

    public string? Code { get; set; }

    public string? Detail { get; set; }

    public ReadOnlyMemory<byte>? Content { get; set; }
}

// Uploads files one request each, one after another, so that each has a state of its own and a refused file stops no
// other (spec 4.14). The state is per file and not per byte: the browser's fetch reports no progress of an upload. Each
// file is read when it arrives, since the file input forgets the files of a choice once the next one is made.
internal sealed class MediaUploadQueue(MediaApi api)
{
    // The default limit of a request of the host (FluentCms:Media:MaxRequestBodySize); a larger file is refused unread.
    public const long MaxFileBytes = 256L * 1024 * 1024;

    public const string TooLarge = "FCMS1582";

    private readonly List<MediaUploadItem> _items = [];

    private bool _running;

    public IReadOnlyList<MediaUploadItem> Items => _items;

    public bool Running => _running;

    public void Clear() => _items.RemoveAll(item => item.State is MediaUploadState.Done or MediaUploadState.Refused);

    // Adds the files, read into memory, and uploads every waiting file, each into the folder it was added to; changed runs
    // after each state. False when the uploads that were already running take the files.
    public async Task<bool> AddAsync(IReadOnlyList<IBrowserFile> files, Guid? folder, Func<Task> changed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(changed);
        foreach (var file in files)
        {
            var item = new MediaUploadItem(file.Name, folder);
            _items.Add(item);
            if (file.Size > MaxFileBytes)
            {
                (item.State, item.Code) = (MediaUploadState.Refused, TooLarge);
                continue;
            }

            try
            {
                await using var stream = file.OpenReadStream(MaxFileBytes, cancellationToken);
                using var memory = new MemoryStream((int)file.Size);
                await stream.CopyToAsync(memory, cancellationToken);
                item.Content = memory.ToArray();
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                (item.State, item.Detail) = (MediaUploadState.Refused, failure.Message);
            }
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
                await UploadAsync(next, cancellationToken);
                await changed();
            }
        }
        finally
        {
            _running = false;
        }

        return true;
    }

    private async Task UploadAsync(MediaUploadItem item, CancellationToken cancellationToken)
    {
        try
        {
            await api.UploadAsync(item.Folder, item.FileName, item.Content!.Value, cancellationToken);
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
        finally
        {
            item.Content = null;
        }
    }
}
