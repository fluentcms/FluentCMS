using FluentCMS.Providers;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Services;

public interface IFileService : IAutoRegisterService
{
    Task<Asset> Create(Guid? folderId, IFormFile formFile, CancellationToken cancellationToken = default);
    Task<Asset> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<Asset> Delete(Guid id, CancellationToken cancellationToken = default);
    Task<Stream> GetStream(Guid id, CancellationToken cancellationToken = default);
}

public class FileService(IAssetRepository assetRepository, IFileStorageProvider fileStorageProvider) : IFileService
{
    public async Task<Asset> Create(Guid? folderId, IFormFile formFile, CancellationToken cancellationToken = default)
    {
        // check if parent folder exists
        if (folderId != null)
        {
            var parentFolder = await assetRepository.GetById(folderId.Value, cancellationToken);
            if (parentFolder == null || parentFolder.Type != AssetType.Folder)
                throw new AppException(ExceptionCodes.FolderParentFolderNotFound);
        }

        var asset = new Asset
        {
            FolderId = folderId,
            Name = formFile.FileName,
            Size = formFile.Length,
            Type = AssetType.File,
            MetaData = new AssetMetaData
            {
                Extension = Path.GetExtension(formFile.FileName),
                MimeType = formFile.ContentType
            }
        };

        await assetRepository.Create(asset, cancellationToken);

        await fileStorageProvider.Upload(asset.Id.ToString(), formFile.OpenReadStream(), cancellationToken);

        // todo: update folder size

        return asset;
    }

    public async Task<Asset> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var existing = await GetById(id, cancellationToken);
        if (existing.Type == AssetType.Folder)
            throw new AppException(ExceptionCodes.FolderNotFound);

        // todo: update folder size

        return await assetRepository.Delete(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.FileUnableToDelete);
    }

    public async Task<Asset> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        return await assetRepository.GetById(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.FileNotFound);
    }

    public async Task<Stream> GetStream(Guid id, CancellationToken cancellationToken = default)
    {
        return await fileStorageProvider.Download(id.ToString(), cancellationToken) ??
            throw new AppException(ExceptionCodes.FileNotFound);
    }
}
