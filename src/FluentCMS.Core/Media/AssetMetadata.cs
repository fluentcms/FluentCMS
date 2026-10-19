using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Media;

// What people and agents say about an asset (MED-01, MED-02): its file name, the folder it sits in, its tags, and the alt text and
// caption pages show with it. Folder is "" for the root or slug segments joined by slashes, such as "products/drills".
public sealed record AssetMetadata(string FileName, string Folder, IReadOnlyList<string> Tags, string? AltText, string? Caption)
{
    public const int MaxFileNameLength = 255;
    public const int MaxFolderLength = 512;
    public const int MaxTags = 32;
    public const int MaxAltTextLength = 1000;
    public const int MaxCaptionLength = 2000;

    // Every problem at once, each naming its field (UX-03). The metadata returned is the one to store: trimmed, with tags in lowercase,
    // each once, in the order given.
    public static Result<AssetMetadata> Create(string fileName, string? folder, IEnumerable<string>? tags, string? altText, string? caption)
    {
        var errors = new List<Error>();
        var name = fileName?.Trim() ?? string.Empty;
        var path = folder?.Trim().Trim('/') ?? string.Empty;
        var tagList = (tags ?? []).Select(tag => tag?.Trim().ToLowerInvariant() ?? string.Empty).Distinct(StringComparer.Ordinal).ToList();

        if (name.Length is 0 or > MaxFileNameLength || name is "." or ".." || name.Any(character => character is '/' or '\\' || char.IsControl(character)))
        {
            errors.Add(new Error(
                MediaErrorCodes.FileNameInvalid,
                $"fileName: the file name must not be blank, must be at most {MaxFileNameLength} characters and must not contain slashes, such as 'drill.jpg'."));
        }

        if (path.Length > MaxFolderLength || (path.Length > 0 && !path.Split('/').All(SchemaNames.IsSlug)))
        {
            errors.Add(new Error(
                MediaErrorCodes.FolderInvalid,
                $"folder: a folder is lowercase words joined by hyphens, with slashes between levels, such as 'products/drills', at most {MaxFolderLength} characters; leave it out for the root."));
        }

        if (tagList.Count > MaxTags || !tagList.All(SchemaNames.IsSlug))
        {
            errors.Add(new Error(
                MediaErrorCodes.TagInvalid,
                $"tags: give at most {MaxTags} tags, each lowercase words joined by hyphens, such as 'product-shot'."));
        }

        if (!IsValidText(altText, MaxAltTextLength))
        {
            errors.Add(new Error(
                MediaErrorCodes.AltTextInvalid,
                $"altText: alt text describes the image for people who cannot see it; it must not be blank and must be at most {MaxAltTextLength} characters. Leave it out instead."));
        }

        if (!IsValidText(caption, MaxCaptionLength))
        {
            errors.Add(new Error(
                MediaErrorCodes.CaptionInvalid,
                $"caption: a caption must not be blank and must be at most {MaxCaptionLength} characters; leave it out instead."));
        }

        return errors.Count == 0
            ? Result<AssetMetadata>.Success(new AssetMetadata(name, path, tagList, altText?.Trim(), caption?.Trim()))
            : Result<AssetMetadata>.Failure(errors);
    }

    private static bool IsValidText(string? text, int maxLength)
    {
        return text is null || (!string.IsNullOrWhiteSpace(text) && text.Trim().Length <= maxLength);
    }
}
