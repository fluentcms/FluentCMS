namespace FluentCMS.Services;

public interface IFolderService : IAutoRegisterService
{
    Task<Asset> Create(string folderName, Guid? parentFolderId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Asset>> GetByParentId(Guid? parentFolderId, CancellationToken cancellationToken = default);
    Task<Asset> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<Asset> Update(Guid id, string folderName, Guid? parentFolderId, CancellationToken cancellationToken = default);
    Task<Asset> Delete(Guid id, CancellationToken cancellationToken = default);
}


public class FolderService(IAssetRepository assetRepository) : IFolderService
{
    public async Task<Asset> Create(string folderName, Guid? parentFolderId, CancellationToken cancellationToken = default)
    {
        var asset = new Asset
        {
            Name = folderName,
            ParentId = parentFolderId,
            Type = AssetType.Folder,
            Size = 0,
        };

        // check if parent folder exists
        if (parentFolderId != null)
        {
            var parentFolder = await assetRepository.GetById(parentFolderId.Value, cancellationToken);
            if (parentFolder == null || parentFolder.Type != AssetType.Folder)
                throw new AppException(ExceptionCodes.FolderParentFolderNotFound);
        }
        return await assetRepository.Create(asset, cancellationToken) ??
            throw new AppException(ExceptionCodes.FolderUnableToCreate);
    }

    public async Task<IEnumerable<Asset>> GetByParentId(Guid? parentFolderId, CancellationToken cancellationToken = default)
    {
        return await assetRepository.GetByParentId(parentFolderId, cancellationToken);
    }

    public async Task<Asset> Update(Guid id, string folderName, Guid? parentFolderId, CancellationToken cancellationToken = default)
    {
        var existing = await assetRepository.GetById(id, cancellationToken);
        if (existing == null || existing.Type != AssetType.Folder)
            throw new AppException(ExceptionCodes.FolderNotFound);

        // check if parent folder exists
        if (parentFolderId != null)
        {
            var parentFolder = await assetRepository.GetById(parentFolderId.Value, cancellationToken);
            if (parentFolder == null || parentFolder.Type != AssetType.Folder)
                throw new AppException(ExceptionCodes.FolderParentFolderNotFound);
        }

        existing.Name = folderName;
        existing.ParentId = parentFolderId;

        return await assetRepository.Update(existing, cancellationToken) ??
            throw new AppException(ExceptionCodes.FolderUnableToUpdate);
    }

    public async Task<Asset> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        // todo: think about children
        return await assetRepository.Delete(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.FolderUnableToDelete);
    }

    public async Task<Asset> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        return await assetRepository.GetById(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.FolderNotFound);
    }
}
