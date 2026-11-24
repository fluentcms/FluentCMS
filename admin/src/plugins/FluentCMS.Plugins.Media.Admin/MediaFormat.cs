using System.Globalization;
using FluentCMS.Plugins.Media.Admin.Api;
using Microsoft.Extensions.Localization;

namespace FluentCMS.Plugins.Media.Admin;

// How the library shows an asset's type, size and dimensions, in the user's culture.
internal static class MediaFormat
{
    // The resources of the units, a number in each: a translation writes the unit as its language does.
    private static readonly string[] Units = ["Media.Size.Bytes", "Media.Size.Kilobytes", "Media.Size.Megabytes", "Media.Size.Gigabytes"];

    public static string Number(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    public static string Size(long bytes, IStringLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < Units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return localizer[Units[unit], unit == 0 ? Number(bytes) : size.ToString("0.#", CultureInfo.CurrentCulture)];
    }

    // The subtype in capitals, such as PNG for image/png, which is what a file's type is known by.
    public static string Type(string mimeType)
    {
        var subtype = mimeType.Split('/', 2) is [_, var rest] ? rest.Split(';', 2)[0].Split('+', 2)[0] : mimeType;
        return subtype.ToUpperInvariant();
    }

    public static string? Dimensions(MediaAssetInfo asset) =>
        asset is { Width: { } width, Height: { } height } ? $"{Number(width)} × {Number(height)}" : null;

    public static string TypeFilter(string type, IStringLocalizer localizer) => type switch
    {
        "image/*" => localizer["Media.Type.Images"],
        "video/*" => localizer["Media.Type.Videos"],
        "audio/*" => localizer["Media.Type.Audio"],
        "application/pdf" => localizer["Media.Type.Pdf"],
        _ => type,
    };

    // A URL of the Management API is relative to the base of the CMS, which the admin knows as the API's base address.
    public static string Resolve(Uri baseAddress, string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https")
        {
            return absolute.AbsoluteUri;
        }

        var root = baseAddress.AbsoluteUri.EndsWith('/') ? baseAddress : new Uri(baseAddress.AbsoluteUri + "/");
        return new Uri(root, url.TrimStart('/')).AbsoluteUri;
    }
}

// The folders of a site, as GET /media/folders lists them, flat with their parents.
internal static class MediaFolders
{
    // The folder and each folder above it.
    public static IEnumerable<Guid> Ancestors(IReadOnlyList<MediaFolderInfo> folders, Guid? folder)
    {
        var byId = folders.ToDictionary(item => item.Id);
        HashSet<Guid> seen = [];
        while (folder is { } id && seen.Add(id) && byId.TryGetValue(id, out var found))
        {
            yield return id;
            folder = found.ParentId;
        }
    }

    // The folder and each folder below it, where a folder may not move.
    public static IReadOnlySet<Guid> WithDescendants(IReadOnlyList<MediaFolderInfo> folders, Guid folder)
    {
        HashSet<Guid> found = [folder];
        bool added;
        do
        {
            added = false;
            foreach (var item in folders)
            {
                if (item.ParentId is { } parent && found.Contains(parent) && found.Add(item.Id))
                {
                    added = true;
                }
            }
        }
        while (added);

        return found;
    }

    // The names from the root down, as "Photos / 2026", which tells two folders of the same name apart.
    public static string Path(IReadOnlyList<MediaFolderInfo> folders, Guid folder) =>
        string.Join(" / ", Ancestors(folders, folder).Reverse().Select(id => folders.First(item => item.Id == id).Name));
}
