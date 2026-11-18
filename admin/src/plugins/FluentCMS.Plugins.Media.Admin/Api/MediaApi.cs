using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;

namespace FluentCMS.Plugins.Media.Admin.Api;

// The media routes of the Management API (spec 4.14) over the admin's one client, which sends the token, the site and the
// locale. A failure is the client's: ApiProblemException for an answer, HttpRequestException and the rest for none.
internal sealed class MediaApi(IManagementApi api)
{
    public const string Root = "root";

    public async Task<MediaAssetPage> ListAsync(MediaListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var answer = await api.SendAsync(new ApiRequest(HttpMethod.Get, "media" + query.ToQueryString()), MediaJsonContext.Default.MediaAssetPage, cancellationToken);
        return answer.Value;
    }

    public async Task<IReadOnlyList<MediaFolderInfo>> FoldersAsync(CancellationToken cancellationToken)
    {
        var answer = await api.SendAsync(new ApiRequest(HttpMethod.Get, "media/folders"), MediaJsonContext.Default.ItemListMediaFolderInfo, cancellationToken);
        return answer.Value.Items;
    }

    // One file a request, so that the refusal of one does not refuse the others, as an upload of several would.
    public async Task<MediaAssetSaved> UploadAsync(MediaUploadBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        var request = new ApiRequest(HttpMethod.Post, "media") { Content = body.Content };
        var answer = await api.SendAsync(request, MediaJsonContext.Default.ItemListMediaAssetSaved, cancellationToken);
        return answer.Value.Items[0];
    }

    public async Task<MediaAssetSaved> MoveAsync(Guid asset, Guid? folder, CancellationToken cancellationToken)
    {
        var request = new ApiRequest(HttpMethod.Post, $"media/{asset:D}/move") { Body = new JsonObject { ["folderId"] = folder?.ToString("D") } };
        return (await api.SendAsync(request, MediaJsonContext.Default.MediaAssetSaved, cancellationToken)).Value;
    }

    // The asset with its alt text and caption in the locale named, or the view's.
    public async Task<MediaAssetInfo> GetAsync(Guid asset, string? locale, CancellationToken cancellationToken)
    {
        var request = new ApiRequest(HttpMethod.Get, $"media/{asset:D}") { Locale = locale };
        return (await api.SendAsync(request, MediaJsonContext.Default.MediaAssetInfo, cancellationToken)).Value;
    }

    // Saves changes of the metadata in the locale the asset was read in, and answers the asset as it is after them. The
    // server keeps no version of an asset and takes no If-Match on it, so the asset is read right before the save: when it
    // changed since basis was read, the save is not sent and MediaConflictException holds the asset as it is now.
    public async Task<MediaAssetInfo> UpdateAsync(MediaAssetInfo basis, JsonObject changes, bool overwrite, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(basis);
        ArgumentNullException.ThrowIfNull(changes);
        var current = await GetAsync(basis.Id, basis.Locale, cancellationToken);
        if (!overwrite && !SameVersion(basis.UpdatedAt, current.UpdatedAt))
        {
            throw new MediaConflictException(current);
        }

        var request = new ApiRequest(HttpMethod.Patch, $"media/{basis.Id:D}") { Body = changes, Locale = current.Locale };
        var changed = (await api.SendAsync(request, MediaJsonContext.Default.MediaAssetChanged, cancellationToken)).Value;

        // A private asset's URLs are signed for it, so a change of the flag reads them again.
        return changed.IsPrivate == current.IsPrivate ? Merge(current, changed) : await GetAsync(basis.Id, current.Locale, cancellationToken);
    }

    public async Task<MediaAssetInfo> ReplaceAsync(Guid asset, MediaUploadBody body, string? locale, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await api.SendAsync(new ApiRequest(HttpMethod.Post, $"media/{asset:D}/replace") { Content = body.Content }, MediaJsonContext.Default.MediaAssetChanged, cancellationToken);
        return await GetAsync(asset, locale, cancellationToken);
    }

    public Task DeleteAsync(Guid asset, CancellationToken cancellationToken) =>
        api.SendAsync(new ApiRequest(HttpMethod.Delete, $"media/{asset:D}"), cancellationToken);

    public async Task<IReadOnlyList<MediaUsageInfo>> UsageAsync(Guid asset, CancellationToken cancellationToken) =>
        (await api.SendAsync(new ApiRequest(HttpMethod.Get, $"media/{asset:D}/usage"), MediaJsonContext.Default.ItemListMediaUsageInfo, cancellationToken)).Value.Items;

    public async Task<MediaDownloadInfo> DownloadUrlAsync(Guid asset, CancellationToken cancellationToken) =>
        (await api.SendAsync(new ApiRequest(HttpMethod.Post, $"media/{asset:D}/download-url"), MediaJsonContext.Default.MediaDownloadInfo, cancellationToken)).Value;

    public async Task<IReadOnlyList<MediaPresetInfo>> PresetsAsync(CancellationToken cancellationToken) =>
        (await api.SendAsync(new ApiRequest(HttpMethod.Get, "media/presets"), MediaJsonContext.Default.ItemListMediaPresetInfo, cancellationToken)).Value.Items;

    public async Task<MediaPresetInfo> SetPresetAsync(string name, MediaPresetOptions options, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(options);
        var body = JsonSerializer.SerializeToNode(options, MediaJsonContext.Default.MediaPresetOptions)!.AsObject();
        foreach (var empty in body.Where(pair => pair.Value is null).Select(pair => pair.Key).ToList())
        {
            body.Remove(empty);
        }

        var request = new ApiRequest(HttpMethod.Put, $"media/presets/{Uri.EscapeDataString(name)}") { Body = body };
        return (await api.SendAsync(request, MediaJsonContext.Default.MediaPresetInfo, cancellationToken)).Value;
    }

    public Task DeletePresetAsync(string name, CancellationToken cancellationToken) =>
        api.SendAsync(new ApiRequest(HttpMethod.Delete, $"media/presets/{Uri.EscapeDataString(name)}"), cancellationToken);

    public async Task<MediaSchema> SchemaAsync(CancellationToken cancellationToken) =>
        (await api.SendAsync(new ApiRequest(HttpMethod.Get, "schema") { NoLocale = true }, MediaJsonContext.Default.MediaSchema, cancellationToken)).Value;

    // The entries of one type among ids, with their id and the field named, in one list read (spec 2.7). An entry the
    // caller may not read is not in the answer.
    public async Task<IReadOnlyList<JsonObject>> EntriesAsync(string type, IReadOnlyCollection<Guid> ids, string? field, string? locale, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        ArgumentNullException.ThrowIfNull(ids);
        var fields = field is null ? "id" : "id," + Uri.EscapeDataString(field);
        var path = $"content/{Uri.EscapeDataString(type)}?filter[id][in]={string.Join(',', ids.Select(id => id.ToString("D")))}"
            + $"&fields={fields}&pageSize={ids.Count.ToString(CultureInfo.InvariantCulture)}";
        var request = locale is null ? new ApiRequest(HttpMethod.Get, path) { NoLocale = true } : new ApiRequest(HttpMethod.Get, path) { Locale = locale };
        return (await api.SendAsync(request, MediaJsonContext.Default.ItemListJsonObject, cancellationToken)).Value.Items;
    }

    // The server writes the time of a change at the precision of its database, which may be coarser than its answer to the
    // change, so two times within a millisecond are the same version.
    private static bool SameVersion(DateTimeOffset? basis, DateTimeOffset? current) =>
        basis is null || current is null || (basis.Value - current.Value).Duration() < TimeSpan.FromMilliseconds(1);

    private static MediaAssetInfo Merge(MediaAssetInfo current, MediaAssetChanged changed) => current with
    {
        Title = changed.Title,
        Locale = changed.Locale ?? current.Locale,
        Alt = changed.Alt,
        Caption = changed.Caption,
        Credit = changed.Credit,
        Tags = changed.Tags,
        FocalPoint = changed.FocalPoint,
        IsPrivate = changed.IsPrivate,
        UpdatedAt = changed.UpdatedAt,
    };

    public async Task<MediaFolderInfo> CreateFolderAsync(string name, Guid? parent, CancellationToken cancellationToken)
    {
        var request = new ApiRequest(HttpMethod.Post, "media/folders")
        {
            Body = new JsonObject { ["name"] = name, ["parentId"] = parent?.ToString("D") },
        };
        return (await api.SendAsync(request, MediaJsonContext.Default.MediaFolderInfo, cancellationToken)).Value;
    }

    public async Task<MediaFolderInfo> RenameFolderAsync(Guid folder, string name, CancellationToken cancellationToken)
    {
        var request = new ApiRequest(HttpMethod.Patch, $"media/folders/{folder:D}") { Body = new JsonObject { ["name"] = name } };
        return (await api.SendAsync(request, MediaJsonContext.Default.MediaFolderInfo, cancellationToken)).Value;
    }

    public async Task<MediaFolderInfo> MoveFolderAsync(Guid folder, Guid? parent, CancellationToken cancellationToken)
    {
        var request = new ApiRequest(HttpMethod.Post, $"media/folders/{folder:D}/move") { Body = new JsonObject { ["parentId"] = parent?.ToString("D") } };
        return (await api.SendAsync(request, MediaJsonContext.Default.MediaFolderInfo, cancellationToken)).Value;
    }

    public Task DeleteFolderAsync(Guid folder, CancellationToken cancellationToken) =>
        api.SendAsync(new ApiRequest(HttpMethod.Delete, $"media/folders/{folder:D}"), cancellationToken);
}

// A save of an asset's metadata that was not sent, because the asset changed since the read it was based on.
internal sealed class MediaConflictException(MediaAssetInfo current) : Exception("The asset changed since it was read.")
{
    public MediaAssetInfo Current { get; } = current;
}

// The query of GET /media. Without a search or a filter of the user the open folder alone is listed; with one, the folder
// and its subfolders, and every folder from the root.
internal sealed record MediaListQuery
{
    public Guid? Folder { get; init; }

    public string? Search { get; init; }

    public string? Type { get; init; }

    public string? Tag { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = MediaBrowseState.PageSize;

    // Whether Type is the caller's limit rather than the user's filter, which leaves the folder's scope as it is.
    public bool TypeIsConstraint { get; init; }

    public bool Filtered => Search is not null || (Type is not null && !TypeIsConstraint) || Tag is not null;

    public string ToQueryString()
    {
        List<string> parts = [];
        if (Folder is { } folder)
        {
            parts.Add("folder=" + folder.ToString("D"));
            if (Filtered)
            {
                parts.Add("subfolders=true");
            }
        }
        else if (!Filtered)
        {
            parts.Add("folder=" + MediaApi.Root);
        }

        Add(parts, "search", Search);
        Add(parts, "type", Type);
        Add(parts, "tag", Tag);
        parts.Add("page=" + Page.ToString(CultureInfo.InvariantCulture));
        parts.Add("pageSize=" + PageSize.ToString(CultureInfo.InvariantCulture));
        return "?" + string.Join('&', parts);
    }

    private static void Add(List<string> parts, string name, string? value)
    {
        if (value is not null)
        {
            parts.Add(name + "=" + Uri.EscapeDataString(value));
        }
    }
}
