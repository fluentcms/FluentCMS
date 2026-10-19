using System.Security.Cryptography;
using FluentCMS.Core.Content;
using FluentCMS.Core.Media;
using FluentCMS.Core.Results;
using FluentCMS.Infrastructure.Media;
using FluentCMS.Infrastructure.Persistence;
using FluentCMS.IntegrationTests.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static FluentCMS.IntegrationTests.Content.EntryScenario;

namespace FluentCMS.IntegrationTests.Media;

public sealed class MediaLibraryTests
{
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_upload_stores_the_bytes_in_the_configured_storage_and_version_one(string provider)
    {
        await using var scenario = await EntryScenario.CreateAsync(provider);
        var bytes = MediaFiles.Jpeg(2048);

        var asset = (await UploadAsync(scenario, bytes, Metadata("drill.jpg", "products/drills", ["product-shot"], "A cordless drill."))).Value;
        var read = (await LibraryAsync(scenario, library => library.GetAsync(asset.Id))).Value;

        Assert.Equal((1, (int?)null), (read.LatestVersion, read.PublishedVersion));
        Assert.Equal(new MediaBlob("image/jpeg", 2048, Convert.ToHexStringLower(SHA256.HashData(bytes)), "local", asset.Latest.Blob.Key), read.Latest.Blob);
        Assert.Equal(("drill.jpg", "products/drills", "A cordless drill."), (read.Latest.Metadata.FileName, read.Latest.Metadata.Folder, read.Latest.Metadata.AltText));
        Assert.Equal(["product-shot"], read.Latest.Metadata.Tags);
        Assert.Equal(bytes, await ContentAsync(scenario, asset.Id));

        var uploaded = Assert.Single(await scenario.Schema.QueryAsync(context => context.AuditEvents.Where(row => row.SubjectType == "media").ToListAsync()));
        Assert.Equal(("media.uploaded", asset.Id), (uploaded.Operation, uploaded.SubjectId!.Value));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_metadata_update_stores_the_next_version_with_the_same_bytes(string provider)
    {
        await using var scenario = await EntryScenario.CreateAsync(provider);
        var bytes = MediaFiles.Jpeg(64);
        var asset = (await UploadAsync(scenario, bytes, Metadata("drill.jpg"))).Value;

        var updated = (await LibraryAsync(scenario, library => library.UpdateAsync(asset.Id, 1, Metadata("drill.jpg", "products", ["hero"], "A drill on a bench.", "Our best seller.")))).Value;
        var stale = await LibraryAsync(scenario, library => library.UpdateAsync(asset.Id, 1, Metadata("other.jpg")));
        var invalid = await LibraryAsync(scenario, library => library.UpdateAsync(asset.Id, 2, new AssetMetadata("a/b.jpg", "Not Valid", [], " ", null)));

        Assert.Equal(2, updated.LatestVersion);
        Assert.Equal(asset.Latest.Blob, updated.Latest.Blob);
        Assert.Equal("Our best seller.", updated.Latest.Metadata.Caption);
        Assert.Equal(MediaErrorCodes.VersionConflict, Assert.Single(stale.Errors).Code);
        Assert.Equal([MediaErrorCodes.FileNameInvalid, MediaErrorCodes.FolderInvalid, MediaErrorCodes.AltTextInvalid], invalid.Errors.Select(error => error.Code));
        Assert.Null((await LibraryAsync(scenario, library => library.GetVersionAsync(asset.Id, 1))).Value.Metadata.AltText);
        Assert.Equal(bytes, await ContentAsync(scenario, asset.Id, 1));
        Assert.Equal(2, await scenario.Schema.QueryAsync(context => context.MediaAssetVersions.CountAsync()));

        var updatedEvent = Assert.Single(await scenario.Schema.QueryAsync(context => context.AuditEvents.Where(row => row.Operation == "media.updated").ToListAsync()));
        var first = await scenario.Schema.QueryAsync(context => context.MediaAssetVersions.SingleAsync(row => row.Version == 1));
        Assert.Equal(first.Id, updatedEvent.PreviousVersionId);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Empty_oversized_and_mistyped_uploads_are_refused_and_leave_no_bytes(string provider)
    {
        await using var scenario = await EntryScenario.CreateAsync(provider, services => services.PostConfigure<MediaOptions>(media => media.MaxBytes = 100));

        var empty = await UploadAsync(scenario, [], Metadata("empty.png"));
        var large = await UploadAsync(scenario, MediaFiles.Jpeg(100_000), Metadata("large.jpg"));
        var mistyped = await UploadAsync(scenario, new byte[10], Metadata("text.png"), contentType: "png");

        Assert.Equal(MediaErrorCodes.MediaEmpty, Assert.Single(empty.Errors).Code);
        Assert.Equal(MediaErrorCodes.MediaTooLarge, Assert.Single(large.Errors).Code);
        Assert.Equal("The file has more than 100 bytes, and an upload may have at most 100.", large.Errors[0].Message);
        Assert.Equal(MediaErrorCodes.ContentTypeInvalid, Assert.Single(mistyped.Errors).Code);
        Assert.Equal(0, await scenario.Schema.QueryAsync(context => context.MediaAssets.CountAsync()));

        var root = scenario.Services.GetRequiredService<IOptions<LocalDiskOptions>>().Value.Root;
        Assert.Empty(Directory.Exists(root) ? Directory.GetFiles(root, "*", SearchOption.AllDirectories) : []);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Documents_a_browser_runs_and_files_that_lie_about_their_type_are_refused_and_leave_no_bytes(string provider)
    {
        await using var scenario = await EntryScenario.CreateAsync(provider);
        var page = "<html><script>alert(1)</script></html>"u8.ToArray();

        var html = await UploadAsync(scenario, page, Metadata("page.html"), contentType: "text/html");
        var svg = await UploadAsync(scenario, "<svg onload=\"alert(1)\"/>"u8.ToArray(), Metadata("logo.svg"), contentType: "image/svg+xml");
        var disguised = await UploadAsync(scenario, page, Metadata("photo.jpg"));
        var wrongImage = await UploadAsync(scenario, MediaFiles.Jpeg(), Metadata("photo.png"), contentType: "image/png");

        Assert.Equal(MediaErrorCodes.ContentTypeInvalid, Assert.Single(html.Errors).Code);
        Assert.Contains("image/jpeg", html.Errors[0].Message);
        Assert.Equal(MediaErrorCodes.ContentTypeInvalid, Assert.Single(svg.Errors).Code);
        Assert.Equal(MediaErrorCodes.ContentMismatch, Assert.Single(disguised.Errors).Code);
        Assert.Equal(MediaErrorCodes.ContentMismatch, Assert.Single(wrongImage.Errors).Code);
        Assert.Equal(0, await scenario.Schema.QueryAsync(context => context.MediaAssets.CountAsync()));

        var root = scenario.Services.GetRequiredService<IOptions<LocalDiskOptions>>().Value.Root;
        Assert.Empty(Directory.Exists(root) ? Directory.GetFiles(root, "*", SearchOption.AllDirectories) : []);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Assets_list_by_folder_and_tag(string provider)
    {
        await using var scenario = await EntryScenario.CreateAsync(provider);
        var root = (await UploadAsync(scenario, MediaFiles.Jpeg(), Metadata("logo.png", tags: ["brand"]))).Value.Id;
        var drills = (await UploadAsync(scenario, MediaFiles.Jpeg(), Metadata("drill.png", "products/drills", ["product-shot"]))).Value.Id;
        var products = (await UploadAsync(scenario, MediaFiles.Jpeg(), Metadata("range.png", "products", ["product-shot", "brand"]))).Value.Id;

        async Task<IReadOnlyList<Guid>> ListAsync(MediaQuery query) =>
            (await LibraryAsync(scenario, library => library.ListAsync(query))).Select(asset => asset.Id).ToList();

        Assert.Equal(new[] { root, drills, products }.Order(), await ListAsync(new MediaQuery()));
        Assert.Equal([products], await ListAsync(new MediaQuery("products")));
        Assert.Equal(new[] { drills, products }.Order(), await ListAsync(new MediaQuery("products", IncludeSubfolders: true)));
        Assert.Equal([root], await ListAsync(new MediaQuery("")));
        Assert.Equal(new[] { root, products }.Order(), await ListAsync(new MediaQuery(Tag: "brand")));
        Assert.Equal([drills], await ListAsync(new MediaQuery("products/drills", Tag: "product-shot")));
        Assert.Equal(["", "products", "products/drills"], await LibraryAsync(scenario, library => library.FoldersAsync()));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_entry_must_reference_an_existing_asset_and_then_uses_it(string provider)
    {
        await using var scenario = await EntryScenario.CreateAsync(provider);
        await scenario.Schema.CreateReferenceModelAsync();
        var hero = (await UploadAsync(scenario, MediaFiles.Jpeg(), Metadata("drill.jpg"))).Value.Id;
        var missing = Guid.CreateVersion7();

        var refused = await scenario.CreateAsync("product", $$"""{"name": {{Fact("\"Drill\"")}}, "price": {{Fact("99")}}, "hero": {{Fact($"\"{missing}\"")}}}""");
        var product = (await scenario.CreateAsync("product", $$"""{"name": {{Fact("\"Drill\"")}}, "price": {{Fact("99")}}, "hero": {{Fact($"\"{hero}\"")}}}""")).Value.Id;

        Assert.Equal(ContentErrorCodes.ReferenceNotFound, Assert.Single(refused.Errors).Code);
        Assert.Contains("not a media asset", refused.Errors[0].Message);
        await using var scope = scenario.Services.CreateAsyncScope();
        var usage = Assert.Single(await scope.ServiceProvider.GetRequiredService<IWhereUsed>().ForMediaAsync(hero));
        Assert.Equal((product, "/hero/value"), (usage.EntryId, Assert.Single(usage.Paths)));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_stored_version_cannot_be_overwritten_and_unknown_assets_fail(string provider)
    {
        await using var scenario = await EntryScenario.CreateAsync(provider);
        await UploadAsync(scenario, MediaFiles.Jpeg(), Metadata("drill.jpg"));
        var unknown = Guid.CreateVersion7();

        await scenario.Schema.QueryAsync(async context =>
        {
            await Assert.ThrowsAnyAsync<Exception>(() => context.MediaAssetVersions.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.AltText, "Tampered")));
            return 0;
        });

        Assert.Null((await scenario.Schema.QueryAsync(context => context.MediaAssetVersions.SingleAsync())).AltText);
        Assert.Equal(MediaErrorCodes.AssetNotFound, Assert.Single((await LibraryAsync(scenario, library => library.GetAsync(unknown))).Errors).Code);
        Assert.Equal(MediaErrorCodes.AssetNotFound, Assert.Single((await LibraryAsync(scenario, library => library.OpenAsync(unknown))).Errors).Code);
        Assert.Equal(MediaErrorCodes.AssetNotFound, Assert.Single((await LibraryAsync(scenario, library => library.UpdateAsync(unknown, 1, Metadata("x.png")))).Errors).Code);
    }

    private static AssetMetadata Metadata(string fileName, string? folder = null, string[]? tags = null, string? altText = null, string? caption = null)
    {
        return AssetMetadata.Create(fileName, folder, tags, altText, caption).Value;
    }

    private static Task<Result<MediaAsset>> UploadAsync(EntryScenario scenario, byte[] bytes, AssetMetadata metadata, string contentType = "image/jpeg")
    {
        return LibraryAsync(scenario, library => library.UploadAsync(new NewAsset(new MemoryStream(bytes), contentType, metadata)));
    }

    private static async Task<byte[]> ContentAsync(EntryScenario scenario, Guid asset, int? version = null)
    {
        var content = (await LibraryAsync(scenario, library => library.OpenAsync(asset, version))).Value;
        await using var stream = content.Content;
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        return copy.ToArray();
    }

    private static async Task<T> LibraryAsync<T>(EntryScenario scenario, Func<IMediaLibrary, Task<T>> action)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IMediaLibrary>());
    }
}
