using System.Globalization;
using System.Net;
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
        var answer = await api.SendAsync(request, MediaJsonContext.Default.MediaAssetSaved, cancellationToken);
        return answer.Value with { ETag = answer.EntityTag };
    }

    // The asset with its alt text and caption in the locale named, or the view's.
    public async Task<MediaAssetInfo> GetAsync(Guid asset, string? locale, CancellationToken cancellationToken)
    {
        var request = new ApiRequest(HttpMethod.Get, $"media/{asset:D}") { Locale = locale };
        var answer = await api.SendAsync(request, MediaJsonContext.Default.MediaAssetInfo, cancellationToken);
        return answer.Value with { ETag = answer.EntityTag };
    }

    // Saves changes of the metadata in the locale the asset was read in, with the ETag of basis as If-Match, and answers
    // the asset as it is after them. When the asset changed since, the server refuses the save with 412 and
    // MediaConflictException holds the asset as a read after the refusal finds it; a save over that change is based on it.
    public async Task<MediaAssetInfo> UpdateAsync(MediaAssetInfo basis, JsonObject changes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(basis);
        ArgumentNullException.ThrowIfNull(changes);
        var request = new ApiRequest(HttpMethod.Patch, $"media/{basis.Id:D}") { Body = changes, Locale = basis.Locale, IfMatch = basis.ETag };
        ApiResponse<MediaAssetChanged> answer;
        try
        {
            answer = await api.SendAsync(request, MediaJsonContext.Default.MediaAssetChanged, cancellationToken);
        }
        catch (ApiProblemException refused) when (refused.Problem.Status == (int)HttpStatusCode.PreconditionFailed)
        {
            throw new MediaConflictException(await GetAsync(basis.Id, basis.Locale, cancellationToken));
        }

        // A private asset's URLs are signed for it, so a change of the flag reads them again. When that read fails the save
        // is still done, and the asset keeps the answer's ETag, so that the next save is not a conflict with this one.
        var saved = Merge(basis, answer.Value) with { ETag = answer.EntityTag };
        if (answer.Value.IsPrivate == basis.IsPrivate)
        {
            return saved;
        }

        try
        {
            return await GetAsync(basis.Id, basis.Locale, cancellationToken);
        }
        catch (Exception failure) when (failure is ApiProblemException or HttpRequestException or TaskCanceledException or JsonException)
        {
            return saved;
        }
    }

    // The answer has no URLs, so the caller reads the asset after it; the answer's ETag is what a save is based on when that
    // read fails.
    public async Task<MediaAssetChanged> ReplaceAsync(Guid asset, MediaUploadBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        var answer = await api.SendAsync(new ApiRequest(HttpMethod.Post, $"media/{asset:D}/replace") { Content = body.Content }, MediaJsonContext.Default.MediaAssetChanged, cancellationToken);
        return answer.Value with { ETag = answer.EntityTag };
    }

    // With force, the asset is taken out of the entries that use it (FCMS1594 without).
    public Task DeleteAsync(Guid asset, bool force, CancellationToken cancellationToken) =>
        api.SendAsync(new ApiRequest(HttpMethod.Delete, force ? $"media/{asset:D}?force=true" : $"media/{asset:D}"), cancellationToken);

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

// A save of an asset's metadata that the server refused, because the asset changed since the read it was based on.
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
