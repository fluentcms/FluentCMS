using FluentCMS.Content;

namespace FluentCMS.Media;

/// <summary>
/// The assets, folders and image presets of the media plugin in the site of the current operation, for its endpoints, the
/// media field type and other plugins. Every call but <see cref="GetImageUrlAsync"/> and <see cref="DeliverAsync"/> checks the permission it needs
/// (<c>media.read</c>, <c>media.upload</c>, <c>media.update</c>, <c>media.delete</c>, <c>media.folders.manage</c> or
/// <c>media.presets.manage</c>) and throws <see cref="Authorization.PermissionDeniedException"/> without it. A grant of one of them may be limited to folders, and then reaches those folders and their subfolders only.
/// An asset or folder of another site, or outside the caller's folders, is not found. Refusals are
/// <see cref="Plugins.PluginException"/>s with the plugin's codes.
/// </summary>
public interface IMediaService
{
    /// <summary>
    /// Stores files as new assets, all of them or none. Each file's type is read from its bytes, never from its name, and must
    /// be on the site's allow-list; executables are always refused, and an SVG is sanitised or refused as the site's media
    /// settings say. Size, SHA-256 checksum, and the width and height of images are read on the way.
    /// </summary>
    /// <param name="upload">The files and the folder they go to.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The new assets, in the order of the files.</returns>
    Task<IReadOnlyList<MediaAsset>> UploadAsync(MediaUpload upload, CancellationToken cancellationToken = default);

    /// <summary>Reads an asset, with its alternative text and caption in the locale of the operation.</summary>
    /// <param name="id">The id of the asset.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The asset.</returns>
    /// <exception cref="Plugins.PluginException">No such asset is found in the site or the caller's folders, with the media plugin's code for it, as for every member that names an asset.</exception>
    Task<MediaAsset> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Lists the assets the caller may read, newest first.</summary>
    /// <param name="query">The filters and the page.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The page, with the total.</returns>
    Task<PagedResult<MediaAsset>> ListAsync(MediaQuery query, CancellationToken cancellationToken = default);

    /// <summary>Changes the metadata of an asset; the alternative text and caption are those of the locale of the operation.</summary>
    /// <param name="id">The id of the asset.</param>
    /// <param name="changes">The changes; what is left out stays.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The asset as it is now.</returns>
    Task<MediaAsset> UpdateAsync(Guid id, MediaAssetChanges changes, CancellationToken cancellationToken = default);

    /// <summary>Moves an asset to another folder. It needs <c>media.update</c> in both folders.</summary>
    /// <param name="id">The id of the asset.</param>
    /// <param name="folderId">The folder, or <see langword="null"/> for the root.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The asset as it is now.</returns>
    Task<MediaAsset> MoveAsync(Guid id, Guid? folderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an asset softly: it is found no more, and the daily cleanup removes its bytes and its row once the deleted
    /// retention of the instance has passed. An asset that entries outside the trash refer to is refused, unless
    /// <paramref name="force"/>, which deletes it and removes every reference to it, so that the fields that held it read as empty.
    /// </summary>
    /// <param name="id">The id of the asset.</param>
    /// <param name="force">Whether to delete an asset that entries refer to.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the asset is deleted.</returns>
    Task DeleteAsync(Guid id, bool force, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the file of an asset. It needs <c>media.update</c>. The new file passes the checks of an upload, and its name,
    /// type, size, checksum and image size replace those of the old one; the id, the metadata and the references stay, and
    /// the variants of the old file are removed, so that the next transform reads the new one.
    /// </summary>
    /// <param name="id">The id of the asset.</param>
    /// <param name="file">The new file.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The asset as it is now.</returns>
    Task<MediaAsset> ReplaceAsync(Guid id, MediaUploadFile file, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the fields, blocks and rich text of entries outside the trash that refer to an asset. It needs <c>media.read</c>,
    /// and leaves out the entries, locales and fields the caller may not read.
    /// </summary>
    /// <param name="id">The id of the asset.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The references.</returns>
    Task<IReadOnlyList<ContentReference>> GetUsageAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds a short-lived signed URL of the original file of an asset, private or public,
    /// <c>/media/{id}/original/{fileName}?exp=&amp;sig=</c>, relative to the base of the CMS. It needs <c>media.read</c> in the
    /// asset's folder. The URL lives for the <c>DownloadUrlSeconds</c> of the site's media settings, and serves the file of
    /// the asset as it is when the URL is used.
    /// </summary>
    /// <param name="id">The id of the asset.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The URL and when it expires.</returns>
    Task<MediaDownloadUrl> GetDownloadUrlAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads an asset for the Delivery API, with its alternative text and caption in the locale of the operation. A public
    /// asset is read by any caller, with its URLs; a private one only by a caller whose <c>media.read</c> reaches its folder,
    /// and without URLs. Any other asset is not found.
    /// </summary>
    /// <param name="id">The id of the asset.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The asset and its URLs.</returns>
    Task<MediaDelivery> DeliverAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Lists the folders the caller may read, by name within their parent.</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The folders.</returns>
    Task<IReadOnlyList<MediaFolder>> ListFoldersAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates a folder. Names are unique within their parent.</summary>
    /// <param name="name">The name.</param>
    /// <param name="parentId">The parent, or <see langword="null"/> for the root.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The folder.</returns>
    Task<MediaFolder> CreateFolderAsync(string name, Guid? parentId, CancellationToken cancellationToken = default);

    /// <summary>Renames a folder.</summary>
    /// <param name="id">The id of the folder.</param>
    /// <param name="name">The new name.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The folder as it is now.</returns>
    Task<MediaFolder> RenameFolderAsync(Guid id, string name, CancellationToken cancellationToken = default);

    /// <summary>Moves a folder, with its subfolders and assets, under another parent, which must not be inside it.</summary>
    /// <param name="id">The id of the folder.</param>
    /// <param name="parentId">The new parent, or <see langword="null"/> for the root.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The folder as it is now.</returns>
    Task<MediaFolder> MoveFolderAsync(Guid id, Guid? parentId, CancellationToken cancellationToken = default);

    /// <summary>Deletes a folder that holds no folder and no asset.</summary>
    /// <param name="id">The id of the folder.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the folder is deleted.</returns>
    Task DeleteFolderAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the signed URL of a transform of an image, <c>/media/{id}/{fileName}?…&amp;sig=</c>, relative to the base of
    /// the CMS. It checks no permission and reads no asset: the URL serves only an asset that is public and not deleted,
    /// and only within the limits of the site's media settings, which the request checks.
    /// </summary>
    /// <param name="asset">The asset, whose id and file name the URL carries.</param>
    /// <param name="options">The parameters, or <see langword="null"/> for the image as it is.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The URL, signed with the instance's signing key.</returns>
    Task<string> GetImageUrlAsync(MediaAsset asset, MediaImageOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a copy of an image for code that runs in the host and needs its pixels, such as a plugin that describes the
    /// image: decoded, resized as the parameters say and encoded again without its metadata. It needs <c>media.read</c> in
    /// the asset's folder, reads a private asset too, and is held to the limits of the site's media settings.
    /// </summary>
    /// <param name="id">The id of the asset, an image the image processor reads.</param>
    /// <param name="options">The size, quality and type of the copy. A preset is not taken.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The copy, whose stream the caller disposes.</returns>
    Task<MediaImage> OpenImageAsync(Guid id, MediaImageOptions options, CancellationToken cancellationToken = default);

    /// <summary>Lists the presets of the site, by name. It needs <c>media.read</c>.</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The presets.</returns>
    Task<IReadOnlyList<MediaPreset>> ListPresetsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or replaces a preset of the site. It needs <c>media.presets.manage</c> without a folder limit, and the
    /// parameters must be within the limits of the site's media settings.
    /// </summary>
    /// <param name="name">The name, of lower-case letters, digits and hyphens.</param>
    /// <param name="options">The parameters, without a preset.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The preset as it is now.</returns>
    Task<MediaPreset> SetPresetAsync(string name, MediaImageOptions options, CancellationToken cancellationToken = default);

    /// <summary>Deletes a preset of the site. It needs <c>media.presets.manage</c> without a folder limit.</summary>
    /// <param name="name">The name.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the preset is deleted.</returns>
    Task DeletePresetAsync(string name, CancellationToken cancellationToken = default);
}
