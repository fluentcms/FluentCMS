using FluentCMS.Entities;
using FluentCMS.Providers.Storage;
using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Services;

public interface IAssetService
{
    Task<IEnumerable<Asset>> GetAllSiteAssets(Guid siteId);
    Task<(Asset, Stream)> GetAssetAsStream(Guid id);
    Task<Asset> AddFromStream(Stream stream, string fileNameAndPath, Guid siteId);
    Task DeleteAsset(Guid id);
}

internal class AssetService : BaseService<Asset>, IAssetService
{
    private readonly IFileStorageProvider _fileStorageProvider;
    private readonly IAssetRepository _assetRepository;
    private readonly ISiteRepository _siteRepository;

    public AssetService(
        IApplicationContext appContext,
        IFileStorageProvider fileStorageProvider,
        IAssetRepository assetRepository,
        ISiteRepository siteRepository)
        : base(appContext)
    {
        _fileStorageProvider = fileStorageProvider;
        _assetRepository = assetRepository;
        _siteRepository = siteRepository;
    }

    public async Task<IEnumerable<Asset>> GetAllSiteAssets(Guid siteId)
    {
        var data = await _assetRepository.GetAllOfSite(siteId);
        return data;
    }

    public async Task<(Asset, Stream)> GetAssetAsStream(Guid id)
    {
        var asset = await _assetRepository.GetById(id)
            ?? throw new ApplicationException("Requested asset not found");

        return (asset, await _fileStorageProvider.GetFileStream(asset.PhysicalFileName));
    }

    public async Task<Asset> AddFromStream(Stream stream, string fileNameAndPath, Guid siteId)
    {
        // check site
        var site = await _siteRepository.GetById(siteId);
        if (site == null)
            throw new ApplicationException("Site not found");

        var assetId = Guid.NewGuid();
        var assetExt = Path.GetExtension(fileNameAndPath);
        var saveFileNameAndPath = $"{siteId.ToString().ToLower()}/{assetId.ToString().ToLower()}{assetExt}";

        // check file type
        // todo: get allowed file types from host config
        var allowedFileTypes = new[] { ".jpg", ".jpeg", ".png", ".pdf" };
        if (allowedFileTypes.Contains(assetExt) == false)
        {
            throw new ApplicationException("File type is not allowed");
        }

        // save asset to file storage
        await _fileStorageProvider.SaveFile(stream, saveFileNameAndPath);

        var asset = new Asset
        {
            Id = assetId,
            Extension = assetExt,
            SizeInBytes = stream.Length,
            VirtualFileName = fileNameAndPath,
            PhysicalFileName = saveFileNameAndPath,
            SiteId = siteId,
        };
        PrepareForCreate(asset);
        await _assetRepository.Create(asset);
        return asset;
    }

    public async Task DeleteAsset(Guid id)
    {
        var asset = await _assetRepository.GetById(id)
            ?? throw new ApplicationException("Requested asset not found");

        await _fileStorageProvider.DeleteFile(asset.PhysicalFileName);
        await _assetRepository.Delete(id);
    }
}
