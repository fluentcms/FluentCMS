using FluentCMS.Entities;
using FluentCMS.Providers.Storage;
using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Services;

public interface IAssetService
{
    Task<IEnumerable<Asset>> GetAllSiteAssets(Guid siteId);
    Task<(Asset, Stream)> GetFileAsStream(Guid id);
    Task<Asset> AddFolder(Guid siteId, Guid? parentFolderId, string folderName);
    Task<Asset> AddFileFromStream(Guid siteId, Guid? parentFolderId, string fileName, Stream stream);
    Task DeleteAsset(Guid id);
}

internal class AssetService : BaseService<Asset>, IAssetService
{
    private readonly IFileStorageProvider _fileStorageProvider;
    private readonly IAssetRepository _assetRepository;

    public AssetService(
        IApplicationContext appContext,
        IFileStorageProvider fileStorageProvider,
        IAssetRepository assetRepository)
        : base(appContext)
    {
        _fileStorageProvider = fileStorageProvider;
        _assetRepository = assetRepository;
    }

    public async Task<IEnumerable<Asset>> GetAllSiteAssets(Guid siteId)
    {
        var data = await _assetRepository.GetAllOfSite(siteId);
        return data;
    }

    public async Task<(Asset, Stream)> GetFileAsStream(Guid id)
    {
        var asset = await _assetRepository.GetById(id)
            ?? throw new ApplicationException("Requested asset not found");

        return (asset, await _fileStorageProvider.GetFileStream(asset.PhysicalName));
    }

    public async Task<Asset> AddFolder(Guid siteId, Guid? parentFolderId, string folderName)
    {
        // check for similar assets
        var similarAsset = await _assetRepository.GetAssetByName(siteId, parentFolderId, folderName);
        if (similarAsset != null)
            throw new ApplicationException("Folder with similar name already exists.");

        var assetId = Guid.NewGuid();
        var asset = new Asset
        {
            Id = assetId,
            SiteId = siteId,
            FolderId = parentFolderId,
            Type = AssetType.Folder,
            Name = folderName,
        };
        PrepareForCreate(asset);
        await _assetRepository.Create(asset);
        return asset;
    }

    public async Task<Asset> AddFileFromStream(Guid siteId, Guid? parentFolderId, string fileName, Stream stream)
    {
        var assetId = Guid.NewGuid();
        var assetName = Path.GetFileName(fileName);
        var assetExt = Path.GetExtension(assetName);
        var physicalFileName = $"{siteId.ToString().ToLower()}/{assetId.ToString().ToLower()}{assetExt}";

        // check for similar assets
        var similarAsset = await _assetRepository.GetAssetByName(siteId, parentFolderId, assetName);
        if (similarAsset != null)
            throw new ApplicationException("File with similar name already exists.");

        // check file type
        // todo: get allowed file types from host config
        var allowedFileTypes = new[] { ".jpg", ".jpeg", ".png", ".pdf", ".txt" };
        if (allowedFileTypes.Contains(assetExt) == false)
        {
            throw new ApplicationException("File type is not allowed");
        }

        // save asset to file storage
        await _fileStorageProvider.SaveFile(stream, physicalFileName);

        var asset = new Asset
        {
            Id = assetId,
            SiteId = siteId,
            FolderId = parentFolderId,
            Type = AssetType.File,
            SizeInBytes = stream.Length,
            Name = assetName,
            PhysicalName = physicalFileName,
        };
        PrepareForCreate(asset);
        await _assetRepository.Create(asset);
        return asset;
    }

    public async Task DeleteAsset(Guid id)
    {
        var asset = await _assetRepository.GetById(id)
            ?? throw new ApplicationException("Requested asset not found");

        if (asset.Type == AssetType.File)
        {
            await _fileStorageProvider.DeleteFile(asset.PhysicalName);
        }
        await _assetRepository.Delete(id);
    }
}
