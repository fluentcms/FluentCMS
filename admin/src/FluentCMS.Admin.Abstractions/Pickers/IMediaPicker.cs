namespace FluentCMS.Admin.Pickers;

/// <summary>
/// Lets the user choose assets of the media library in a dialog. The media half provides it, so a component asks for it
/// with <c>IAdminPluginServices.FindAsync</c> and gets none in a site where media is disabled.
/// </summary>
public interface IMediaPicker
{
    /// <summary>Opens the picker.</summary>
    /// <param name="request">What to choose.</param>
    /// <param name="cancellationToken">Closes the picker without a choice.</param>
    /// <returns>
    /// The chosen assets in the order they were chosen, or <see langword="null"/> when the user closed the picker without a
    /// choice.
    /// </returns>
    Task<IReadOnlyList<MediaPickerItem>?> PickAsync(MediaPickerRequest request, CancellationToken cancellationToken = default);
}

/// <summary>What an <see cref="IMediaPicker"/> lets the user choose.</summary>
public sealed class MediaPickerRequest
{
    /// <summary>Gets whether the user may choose several assets.</summary>
    public bool Multiple { get; init; }

    /// <summary>Gets the ids chosen when the picker opens, in their order.</summary>
    public IReadOnlyList<Guid> Selected { get; init; } = [];

    /// <summary>
    /// Gets the media types the user may choose, each a type such as <c>image/png</c> or a group such as <c>image/*</c>;
    /// every type when empty.
    /// </summary>
    public IReadOnlyList<string> Accept { get; init; } = [];

    /// <summary>Gets the content locale the assets' texts are read in; the view's when <see langword="null"/>.</summary>
    public string? Locale { get; init; }
}

/// <summary>An asset chosen with an <see cref="IMediaPicker"/>.</summary>
public sealed class MediaPickerItem
{
    /// <summary>Gets the id of the asset.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the file name.</summary>
    public required string FileName { get; init; }

    /// <summary>Gets the media type, such as <c>image/png</c>.</summary>
    public required string MimeType { get; init; }

    /// <summary>Gets the URL of the original file, as the Management API gave it.</summary>
    public string? Url { get; init; }

    /// <summary>Gets the URL of the thumbnail of an image, or <see langword="null"/> for any other file.</summary>
    public string? ThumbnailUrl { get; init; }

    /// <summary>Gets the alternative text in the locale of the request, if the asset has one.</summary>
    public string? Alt { get; init; }

    /// <summary>Gets the width of an image in pixels.</summary>
    public int? Width { get; init; }

    /// <summary>Gets the height of an image in pixels.</summary>
    public int? Height { get; init; }
}
