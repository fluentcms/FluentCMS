using FluentCMS.Providers.Storage;
using FluentCMS.Services;
using FluentCMS.Tests.Helpers;
using FluentCMS.Tests.Helpers.ApplicationContext;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace FluentCMS.Tests.Services;

public class AssetServiceTests
{
    private readonly IServiceProvider _serviceProvider;
    public AssetServiceTests()
    {
        var services = new ServiceCollection();
        services
            .AddApplicationServices()
            .AddTestApplicationContext()
            .AddInMemoryLiteDbRepositories();

        services.AddTransient<IFileStorageProvider, FakeFileStorageProvider>();
        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task AddFolder_ShouldWork()
    {
        using var scope = _serviceProvider.CreateScope();
        scope.SetupMockApplicationContextForSuperUser();
        var assetService = scope.ServiceProvider.GetRequiredService<IAssetService>();

        var siteId = Guid.NewGuid();
        var folderName = "folder1";
        var asset = await assetService.AddFolder(siteId, null, folderName);

        var getAssetsResult = await assetService.GetAllSiteAssets(siteId);
        getAssetsResult.Where(x => x.Type == Entities.AssetType.Folder && x.Name == folderName).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task AddFile_ShouldWork()
    {
        using var scope = _serviceProvider.CreateScope();
        scope.SetupMockApplicationContextForSuperUser();
        var assetService = scope.ServiceProvider.GetRequiredService<IAssetService>();

        var siteId = Guid.NewGuid();
        var assetId = Guid.Empty;
        var fileName = "text-file.txt";
        using (var fileStream = CreateFakeFileStream())
        {
            var asset = await assetService.AddFileFromStream(siteId, null, fileName, fileStream);
            assetId = asset.Id;
        }

        var (resultAsset, _) = await assetService.GetFileAsStream(assetId);
        resultAsset.ShouldNotBeNull();
        resultAsset.Type.ShouldBe(Entities.AssetType.File);
        resultAsset.Name.ShouldBe(fileName);
    }

    [Fact]
    public async Task AddFileToSubFolder_ShouldWork()
    {
        using var scope = _serviceProvider.CreateScope();
        scope.SetupMockApplicationContextForSuperUser();
        var assetService = scope.ServiceProvider.GetRequiredService<IAssetService>();

        var siteId = Guid.NewGuid();
        var folderAsset1 = await assetService.AddFolder(siteId, null, "folder1");
        var folderAsset2 = await assetService.AddFolder(siteId, folderAsset1.Id, "folder2");
        folderAsset2.FolderId.ShouldBe(folderAsset1.Id);

        var fileAssetId = Guid.Empty;
        var fileName = "text-file.txt";
        using (var fileStream = CreateFakeFileStream())
        {
            var fileAsset = await assetService.AddFileFromStream(siteId, folderAsset2.Id, fileName, fileStream);
            fileAssetId = fileAsset.Id;
        }

        var (resultAsset, _) = await assetService.GetFileAsStream(fileAssetId);
        resultAsset.ShouldNotBeNull();
        resultAsset.Type.ShouldBe(Entities.AssetType.File);
        resultAsset.Name.ShouldBe(fileName);
        resultAsset.FolderId.ShouldBe(folderAsset2.Id);
    }

    private Stream CreateFakeFileStream()
    {
        var memory = new MemoryStream();
        var writer = new StreamWriter(memory);
        writer.WriteLine("Hello World!");
        return memory;
    }
}
