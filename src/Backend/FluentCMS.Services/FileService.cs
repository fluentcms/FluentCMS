namespace FluentCMS.Services;

public interface IFileService : IAutoRegisterService
{
    Task<Asset> Create(Asset asset, CancellationToken cancellationToken = default);
    Task<Asset> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<Asset> Delete(Guid id, CancellationToken cancellationToken = default);
}


public class FileService(IAssetRepository assetRepository) : IFileService
{
    public async Task<Asset> Create(Asset asset, CancellationToken cancellationToken = default)
    {
        asset.Type = AssetType.File;

        // todo: set metadata
        asset.MetaData = new AssetMetaData();

        // check if parent folder exists
        if (asset.ParentId != null)
        {
            var parentFolder = await assetRepository.GetById(asset.ParentId.Value, cancellationToken);
            if (parentFolder == null || parentFolder.Type != AssetType.Folder)
                throw new AppException(ExceptionCodes.FolderParentFolderNotFound);
        }

        // todo: update folder size

        return await assetRepository.Create(asset, cancellationToken) ??
            throw new AppException(ExceptionCodes.FolderUnableToCreate);
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
}
