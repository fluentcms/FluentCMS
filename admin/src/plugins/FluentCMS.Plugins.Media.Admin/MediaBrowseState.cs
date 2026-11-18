using System.Globalization;
using FluentCMS.Plugins.Media.Admin.Api;

namespace FluentCMS.Plugins.Media.Admin;

public enum MediaView
{
    Grid,
    List,
}

// What MediaBrowser shows: the open folder, the view, the search and filters and the page. The library keeps it in the
// address; a picker keeps it in itself.
public sealed record MediaBrowseState
{
    public const int PageSize = 48;

    public const int MaxSearchLength = 256;

    // The type filters offered when the caller names none: a family each, as GET /media takes it.
    public static readonly IReadOnlyList<string> Types = ["image/*", "video/*", "audio/*", "application/pdf"];

    public static MediaBrowseState Default { get; } = new();

    public Guid? Folder { get; init; }

    public MediaView View { get; init; }

    public string? Search { get; init; }

    public string? Type { get; init; }

    public string? Tag { get; init; }

    public int Page { get; init; } = 1;

    internal MediaListQuery Query => new() { Folder = Folder, Search = Search, Type = Type, Tag = Tag, Page = Page };

    // Read as text, so that a malformed value of the address reads as the default instead of failing the page.
    public static MediaBrowseState Parse(string? folder, string? view, string? search, string? type, string? tag, string? page) => new()
    {
        Folder = Guid.TryParse(folder, out var id) ? id : null,
        View = string.Equals(view, "list", StringComparison.OrdinalIgnoreCase) ? MediaView.List : MediaView.Grid,
        Search = Text(search),
        Type = Text(type),
        Tag = Text(tag),
        Page = int.TryParse(page, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 1 ? number : 1,
    };

    // The parameters of the address, the defaults left out.
    public IReadOnlyDictionary<string, object?> ToAddress() => new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["folder"] = Folder?.ToString("D"),
        ["view"] = View == MediaView.List ? "list" : null,
        ["q"] = Search,
        ["type"] = Type,
        ["tag"] = Tag,
        ["page"] = Page > 1 ? Page : null,
    };

    public static string? Text(string? value) =>
        value?.Trim() is { Length: > 0 } trimmed ? trimmed.Length > MaxSearchLength ? trimmed[..MaxSearchLength] : trimmed : null;
}
