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
// library with ReturnQuery, so the page can show each problem next to its field and keep what was typed.
public sealed class MediaUploadForm : MediaMetadataFields
{
    public const string FileMissing = "media_file_missing";

    public const string ErrorName = "error";
    public const string FolderName = "folder";
    public const string TagsName = "tags";
    public const string AltTextName = "alt";
    public const string CaptionName = "caption";

    public Dictionary<string, string?> ReturnQuery(IEnumerable<Error> errors)
    {
        return new Dictionary<string, string?>
        {
            [ErrorName] = string.Join(",", errors.Select(error => error.Code).Distinct()),
            [FolderName] = Folder,
            [TagsName] = Tags,
            [AltTextName] = AltText,
            [CaptionName] = Caption,
        };
    }
}

// The asset page's metadata form. BaseVersion is the version the page showed, so a change made meanwhile is refused, not overwritten.
public sealed class MediaMetadataForm : MediaMetadataFields
{
    public string? FileName { get; set; }

    public int BaseVersion { get; set; }
}
