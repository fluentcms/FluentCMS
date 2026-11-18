using System.Globalization;
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

    // The asset with its title and alternative text in the given content locale, or the view's when null.
    public async Task<MediaAssetInfo> GetAsync(Guid asset, string? locale, CancellationToken cancellationToken)
    {
        var answer = await api.SendAsync(new ApiRequest(HttpMethod.Get, $"media/{asset:D}") { Locale = locale }, MediaJsonContext.Default.MediaAssetInfo, cancellationToken);
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
