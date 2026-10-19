using System.Security.Cryptography;
using FluentCMS.Core.Content;
using FluentCMS.Core.Media;
using FluentCMS.Core.Results;
using FluentCMS.Infrastructure.Persistence;
using FluentCMS.IntegrationTests.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FluentCMS.IntegrationTests.Content.EntryScenario;

namespace FluentCMS.IntegrationTests.Media;

public sealed class MediaSoftDeleteTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_deleted_asset_is_gone_from_every_read_and_cannot_be_referenced(string provider)
    {
        await using var scenario = await CreateAsync(provider, new ManualClock(Start));
        var drill = await UploadAsync(scenario, "drill.jpg", "products");
        var kept = await UploadAsync(scenario, "news.jpg", "blog");

        Assert.True((await LibraryAsync(scenario, library => library.DeleteAsync(drill, 1))).IsSuccess);

        Assert.Equal(MediaErrorCodes.AssetNotFound, Code(await LibraryAsync(scenario, library => library.GetAsync(drill))));
        Assert.Equal(MediaErrorCodes.AssetNotFound, Code(await LibraryAsync(scenario, library => library.GetVersionAsync(drill, 1))));
        Assert.Equal(MediaErrorCodes.AssetNotFound, Code(await LibraryAsync(scenario, library => library.OpenAsync(drill))));
        Assert.Equal(MediaErrorCodes.AssetNotFound, Code(await LibraryAsync(scenario, library => library.UpdateAsync(drill, 1, Metadata("drill.jpg", "products")))));
        Assert.Equal([kept], (await LibraryAsync(scenario, library => library.ListAsync(new MediaQuery()))).Select(asset => asset.Id));
        Assert.Equal(["blog"], await LibraryAsync(scenario, library => library.FoldersAsync()));

        var product = await scenario.CreateAsync("product", $$"""{"name": {{Fact("\"Drill\"")}}, "price": {{Fact("99")}}, "hero": {{Fact($"\"{drill}\"")}}}""");
        Assert.Equal(ContentErrorCodes.ReferenceNotFound, Assert.Single(product.Errors).Code);

        var deleted = Assert.Single(await scenario.Schema.QueryAsync(context => context.AuditEvents.Where(row => row.Operation == "media.deleted").ToListAsync()));
        var version = await scenario.Schema.QueryAsync(context => context.MediaAssetVersions.SingleAsync(row => row.AssetId == drill));
        Assert.Equal((drill, version.Id), (deleted.SubjectId!.Value, deleted.PreviousVersionId!.Value));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Restore_within_the_window_brings_the_asset_back_as_it_was(string provider)
    {
        var clock = new ManualClock(Start);
        await using var scenario = await CreateAsync(provider, clock);
        var drill = await UploadAsync(scenario, "drill.jpg", "products");
        await LibraryAsync(scenario, library => library.UpdateAsync(drill, 1, Metadata("drill.jpg", "products", "A cordless drill.")));
        await scenario.Schema.QueryAsync(context => context.MediaAssets
            .Where(row => row.Id == drill)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PublishedVersion, 1)));
        await LibraryAsync(scenario, library => library.DeleteAsync(drill, 2));

        clock.Now = Start.AddDays(30).AddMinutes(-1);
        var restored = (await LibraryAsync(scenario, library => library.RestoreAsync(drill))).Value;

        Assert.Equal((2, (int?)1, "A cordless drill."), (restored.LatestVersion, restored.PublishedVersion, restored.Latest.Metadata.AltText));
        Assert.Equal(restored.Latest.Blob.Sha256, Convert.ToHexStringLower(SHA256.HashData(await ContentAsync(scenario, drill, 1))));
        Assert.Equal([drill], (await LibraryAsync(scenario, library => library.ListAsync(new MediaQuery()))).Select(asset => asset.Id));
        Assert.Empty(await LibraryAsync(scenario, library => library.ListDeletedAsync()));
        var restoredEvent = Assert.Single(await scenario.Schema.QueryAsync(context => context.AuditEvents.Where(row => row.Operation == "media.restored").ToListAsync()));
        Assert.Equal(drill, restoredEvent.SubjectId);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task After_the_window_restore_fails_and_changes_nothing(string provider)
    {
        var clock = new ManualClock(Start);
        await using var scenario = await CreateAsync(provider, clock);
        var drill = await UploadAsync(scenario, "drill.jpg", "products");
        await LibraryAsync(scenario, library => library.DeleteAsync(drill, 1));
        var events = await scenario.Schema.QueryAsync(context => context.AuditEvents.CountAsync());

        clock.Now = Start.AddDays(30);
        var restore = await LibraryAsync(scenario, library => library.RestoreAsync(drill));

        Assert.Equal(MediaErrorCodes.RetentionExpired, Code(restore));
        Assert.Equal(MediaErrorCodes.AssetNotFound, Code(await LibraryAsync(scenario, library => library.GetAsync(drill))));
        Assert.Equal(events, await scenario.Schema.QueryAsync(context => context.AuditEvents.CountAsync()));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Deletes_and_restores_that_do_not_fit_the_assets_state_fail(string provider)
    {
        await using var scenario = await CreateAsync(provider, new ManualClock(Start));
        var drill = await UploadAsync(scenario, "drill.jpg", "products");
        await LibraryAsync(scenario, library => library.UpdateAsync(drill, 1, Metadata("drill.jpg", "tools")));

        var stale = await LibraryAsync(scenario, library => library.DeleteAsync(drill, 1));
        var notDeleted = await LibraryAsync(scenario, library => library.RestoreAsync(drill));
        var unknownDelete = await LibraryAsync(scenario, library => library.DeleteAsync(Guid.CreateVersion7(), 1));
        var unknownRestore = await LibraryAsync(scenario, library => library.RestoreAsync(Guid.CreateVersion7()));
        Assert.True((await LibraryAsync(scenario, library => library.DeleteAsync(drill, 2))).IsSuccess);
        var twice = await LibraryAsync(scenario, library => library.DeleteAsync(drill, 2));

        Assert.Equal(MediaErrorCodes.VersionConflict, Assert.Single(stale.Errors).Code);
        Assert.Equal(MediaErrorCodes.AssetNotDeleted, Code(notDeleted));
        Assert.Equal(MediaErrorCodes.AssetNotFound, Assert.Single(unknownDelete.Errors).Code);
        Assert.Equal(MediaErrorCodes.AssetNotFound, Code(unknownRestore));
        Assert.Equal(MediaErrorCodes.AssetNotFound, Assert.Single(twice.Errors).Code);
        Assert.Single(await scenario.Schema.QueryAsync(context => context.AuditEvents.Where(row => row.Operation == "media.deleted").ToListAsync()));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task The_deleted_list_says_when_each_asset_was_deleted_and_until_when_it_can_be_restored(string provider)
    {
        var clock = new ManualClock(Start);
        await using var scenario = await CreateAsync(provider, clock);
        var first = await UploadAsync(scenario, "first.jpg", "");
        var second = await UploadAsync(scenario, "second.jpg", "");
        await UploadAsync(scenario, "kept.jpg", "");

        await LibraryAsync(scenario, library => library.DeleteAsync(first, 1));
        clock.Now = Start.AddHours(1);
        await LibraryAsync(scenario, library => library.DeleteAsync(second, 1));

        Assert.Equal(
            [
                new DeletedAsset(second, "second.jpg", 1, Start.AddHours(1), Start.AddHours(1).AddDays(30)),
                new DeletedAsset(first, "first.jpg", 1, Start, Start.AddDays(30)),
            ],
            await LibraryAsync(scenario, library => library.ListDeletedAsync()));
    }

    private static async Task<EntryScenario> CreateAsync(string provider, ManualClock clock)
    {
        var scenario = await EntryScenario.CreateAsync(provider, services => services.AddSingleton<TimeProvider>(clock));
        await scenario.Schema.CreateReferenceModelAsync();
        return scenario;
    }

    private static AssetMetadata Metadata(string fileName, string folder, string? altText = null)
    {
        return AssetMetadata.Create(fileName, folder, [], altText, null).Value;
    }

    private static async Task<Guid> UploadAsync(EntryScenario scenario, string fileName, string folder)
    {
        var asset = await LibraryAsync(scenario, library => library.UploadAsync(new NewAsset(new MemoryStream(MediaFiles.Jpeg()), "image/jpeg", Metadata(fileName, folder))));
        return asset.Value.Id;
    }

    private static async Task<byte[]> ContentAsync(EntryScenario scenario, Guid asset, int version)
    {
        var content = (await LibraryAsync(scenario, library => library.OpenAsync(asset, version))).Value;
        await using var stream = content.Content;
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        return copy.ToArray();
    }

    private static string Code<T>(Result<T> result)
    {
        return Assert.Single(result.Errors).Code;
    }

    private static async Task<T> LibraryAsync<T>(EntryScenario scenario, Func<IMediaLibrary, Task<T>> action)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IMediaLibrary>());
    }
}
