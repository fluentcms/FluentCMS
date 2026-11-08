namespace FluentCMS.Events;

/// <summary>An event about one asset of the media plugin.</summary>
/// <param name="AssetId">The id of the asset.</param>
/// <param name="FolderId">The folder it is in, or <see langword="null"/> for the root.</param>
/// <param name="FileName">The name of its file.</param>
public abstract record MediaAssetEvent(Guid AssetId, Guid? FolderId, string FileName) : CmsEvent;

/// <summary>An asset was uploaded.</summary>
/// <param name="AssetId">The id of the asset.</param>
/// <param name="FolderId">The folder it went to, or <see langword="null"/> for the root.</param>
/// <param name="FileName">The name of its file.</param>
/// <param name="MimeType">The MIME type read from its bytes.</param>
/// <param name="Size">Its length in bytes.</param>
public sealed record MediaAssetUploadedEvent(Guid AssetId, Guid? FolderId, string FileName, string MimeType, long Size) : MediaAssetEvent(AssetId, FolderId, FileName);

/// <summary>The metadata of an asset changed.</summary>
/// <param name="AssetId">The id of the asset.</param>
/// <param name="FolderId">The folder it is in, or <see langword="null"/> for the root.</param>
/// <param name="FileName">The name of its file.</param>
public sealed record MediaAssetUpdatedEvent(Guid AssetId, Guid? FolderId, string FileName) : MediaAssetEvent(AssetId, FolderId, FileName);

/// <summary>An asset moved to another folder.</summary>
/// <param name="AssetId">The id of the asset.</param>
/// <param name="FolderId">The folder it moved to, or <see langword="null"/> for the root.</param>
/// <param name="FileName">The name of its file.</param>
/// <param name="PreviousFolderId">The folder it left, or <see langword="null"/> for the root.</param>
public sealed record MediaAssetMovedEvent(Guid AssetId, Guid? FolderId, string FileName, Guid? PreviousFolderId) : MediaAssetEvent(AssetId, FolderId, FileName);

/// <summary>An asset was deleted.</summary>
/// <param name="AssetId">The id of the asset.</param>
/// <param name="FolderId">The folder it was in, or <see langword="null"/> for the root.</param>
/// <param name="FileName">The name of its file.</param>
public sealed record MediaAssetDeletedEvent(Guid AssetId, Guid? FolderId, string FileName) : MediaAssetEvent(AssetId, FolderId, FileName);

/// <summary>The file of an asset was replaced; its id and metadata stay, and its variants are gone.</summary>
/// <param name="AssetId">The id of the asset.</param>
/// <param name="FolderId">The folder it is in, or <see langword="null"/> for the root.</param>
/// <param name="FileName">The name of its new file.</param>
/// <param name="MimeType">The MIME type read from the new bytes.</param>
/// <param name="Size">The length of the new file in bytes.</param>
public sealed record MediaAssetReplacedEvent(Guid AssetId, Guid? FolderId, string FileName, string MimeType, long Size) : MediaAssetEvent(AssetId, FolderId, FileName);
