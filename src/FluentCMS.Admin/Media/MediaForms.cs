using FluentCMS.Core.Media;
using FluentCMS.Core.Results;

namespace FluentCMS.Admin.Media;

// The metadata fields both media forms post. Tags are typed comma-separated; a blank alt text or caption means none.
public class MediaMetadataFields
{
    public string? Folder { get; set; }

    public string? Tags { get; set; }

    public string? AltText { get; set; }

    public string? Caption { get; set; }

    public Result<AssetMetadata> ToMetadata(string fileName)
    {
        return AssetMetadata.Create(fileName, Folder, SplitTags(Tags), Blank(AltText), Blank(Caption));
    }

    public static IReadOnlyList<string> SplitTags(string? tags)
    {
        return (tags ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static string? Blank(string? text)
    {
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}

// The library's upload form as it posts to the host's upload endpoint; the file arrives beside it. A refused upload comes back to the
// library with ReturnQuery, so the page can show each problem next to its field and keep what was typed. The names are the upload's
// own, so they never filter the list the library shows.
public sealed class MediaUploadForm : MediaMetadataFields
{
    public const string FileMissing = "media_file_missing";

    public const string ErrorName = "upload-error";
    public const string FolderName = "upload-folder";
    public const string TagsName = "upload-tags";
    public const string AltTextName = "upload-alt";
    public const string CaptionName = "upload-caption";
    public const string DroppedName = "upload-dropped";

    // Past this, a field is not carried back, so the redirect stays well inside the server's request line limit.
    public const int MaxReturnedLength = 1500;

    public Dictionary<string, string?> ReturnQuery(IEnumerable<Error> errors)
    {
        var query = new Dictionary<string, string?> { [ErrorName] = string.Join(",", errors.Select(error => error.Code).Distinct()) };
        var dropped = new List<string>();
        foreach (var (name, text) in new[] { (FolderName, Folder), (TagsName, Tags), (AltTextName, AltText), (CaptionName, Caption) })
        {
            if (text is null)
            {
                continue;
            }

            if (Uri.EscapeDataString(text).Length > MaxReturnedLength)
            {
                dropped.Add(name);
                continue;
            }

            query[name] = text;
        }

        if (dropped.Count > 0)
        {
            query[DroppedName] = string.Join(",", dropped);
        }

        return query;
    }
}

// The asset page's metadata form. BaseVersion is the version the page showed, so a change made meanwhile is refused, not overwritten.
public sealed class MediaMetadataForm : MediaMetadataFields
{
    public string? FileName { get; set; }

    public int BaseVersion { get; set; }
}
