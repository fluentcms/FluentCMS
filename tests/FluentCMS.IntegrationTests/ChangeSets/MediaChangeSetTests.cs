using FluentCMS.Core.Agents;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Media;
using FluentCMS.Core.Persistence;
using FluentCMS.Infrastructure.Persistence;
using FluentCMS.IntegrationTests.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FluentCMS.IntegrationTests.ChangeSets.ChangeSetScenario;
using static FluentCMS.IntegrationTests.ChangeSets.EntryChangeSetTests;

namespace FluentCMS.IntegrationTests.ChangeSets;

public sealed class MediaChangeSetTests
{
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Media_operations_are_diffed_published_and_reverted(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var asset = await UploadAsync(scenario, Metadata(null));

        var created = await scenario.OpenAsync(Operation(OperationKind.Create, ItemKind.Media, asset, null, 1));
        var createDiff = Assert.Single((await scenario.RunAsync(directory => directory.DiffAsync(created.Id))).Value.Items);
        await scenario.PublishAsync(created.Id);

        Assert.Equal(["contentType", "length", "sha256"], createDiff.Properties.Select(property => property.Name));
        Assert.Equal(["fileName", "folder", "tags"], createDiff.Fields.Select(field => field.Name));
        Assert.Equal(1, await PublishedMediaVersionAsync(scenario, asset));
        var published = Assert.Single(await scenario.ChangeSetEventsAsync(created.Id), row => row.Operation == "media.published");
        Assert.Equal(("media", asset), (published.SubjectType, published.SubjectId!.Value));

        await UpdateAsync(scenario, asset, 1, Metadata("A cordless drill on a bench."));
        var update = await scenario.PublishAsync(Operation(OperationKind.Update, ItemKind.Media, asset, 1, 2));
        var updateDiff = Assert.Single((await scenario.RunAsync(directory => directory.DiffAsync(update.Id))).Value.Items);

        Assert.Empty(updateDiff.Properties);
        var altText = Assert.Single(updateDiff.Fields);
        Assert.Equal(("altText", ValueChange.Added, "A cordless drill on a bench."), (altText.Name, altText.Change, altText.After!.Value.GetString()));
        Assert.Equal(2, await PublishedMediaVersionAsync(scenario, asset));

        var revert = (await scenario.RunAsync(directory => directory.RevertAsync(update.Id, "The alt text was wrong."))).Value;
        await scenario.PublishAsync(revert.Id);

        Assert.Equal(1, await PublishedMediaVersionAsync(scenario, asset));
        Assert.Equal(ChangeSetState.Reverted, (await scenario.ChangeSetRowAsync(update.Id)).State);

        var delete = await scenario.PublishAsync(Operation(OperationKind.Delete, ItemKind.Media, asset, 1, null));
        Assert.Null(await PublishedMediaVersionAsync(scenario, asset));
        Assert.Single(await scenario.ChangeSetEventsAsync(delete.Id), row => row.Operation == "media.unpublished");
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_stale_base_or_a_failure_mid_publish_moves_nothing(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var asset = await UploadAsync(scenario, Metadata(null));
        var winner = await scenario.ApproveAsync(Operation(OperationKind.Create, ItemKind.Media, asset, null, 1));
        var loser = await scenario.ApproveAsync(Operation(OperationKind.Create, ItemKind.Media, asset, null, 1));

        for (var append = 1; append <= 2; append++)
        {
            scenario.FailOnAppend(append);
            await Assert.ThrowsAsync<InvalidOperationException>(() => scenario.RunAsync(directory => directory.PublishAsync(winner.Id)));
            Assert.Null(await PublishedMediaVersionAsync(scenario, asset));
        }

        Assert.True((await scenario.RunAsync(directory => directory.PublishAsync(winner.Id))).IsSuccess);
        var stale = await scenario.RunAsync(directory => directory.PublishAsync(loser.Id));

        Assert.Equal(ChangeSetErrorCodes.BaseVersionStale, Assert.Single(stale.Errors).Code);
        Assert.Equal(1, await PublishedMediaVersionAsync(scenario, asset));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Media_operations_point_at_stored_versions_and_need_the_agents_operation(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var asset = await UploadAsync(scenario, Metadata(null));
        var set = await scenario.OpenAsync();

        var unknown = await scenario.RunAsync(directory => directory.AddOperationAsync(set.Id, Operation(OperationKind.Create, ItemKind.Media, Guid.NewGuid(), null, 1)));
        var missingVersion = await scenario.RunAsync(directory => directory.AddOperationAsync(set.Id, Operation(OperationKind.Update, ItemKind.Media, asset, 1, 2)));
        Assert.Equal(ChangeSetErrorCodes.ItemNotFound, Assert.Single(unknown.Errors).Code);
        Assert.Equal(ChangeSetErrorCodes.VersionNotFound, Assert.Single(missingVersion.Errors).Code);

        await scenario.UpdateAgentAsync(row =>
        {
            row.Types = ["product"];
            row.Operations = [AgentOperation.Read, AgentOperation.Update];
        });
        scenario.Initiator = Agent;
        var agentSet = await scenario.OpenAsync();

        var refused = await scenario.RunAsync(directory => directory.AddOperationAsync(agentSet.Id, Operation(OperationKind.Create, ItemKind.Media, asset, null, 1)));
        Assert.Equal(AgentPermissionErrorCodes.ScopeDenied, Assert.Single(refused.Errors).Code);

        await scenario.UpdateAgentAsync(row => row.Operations = [AgentOperation.Read, AgentOperation.Create]);
        Assert.True((await scenario.RunAsync(directory => directory.AddOperationAsync(agentSet.Id, Operation(OperationKind.Create, ItemKind.Media, asset, null, 1)))).IsSuccess);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_deleted_asset_fails_the_publish_of_its_set_and_joins_no_other(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var asset = await UploadAsync(scenario, Metadata(null));
        var approved = await scenario.ApproveAsync(Operation(OperationKind.Create, ItemKind.Media, asset, null, 1));

        Assert.True((await LibraryAsync(scenario, library => library.DeleteAsync(asset, 1))).IsSuccess);
        var publish = await scenario.RunAsync(directory => directory.PublishAsync(approved.Id));
        var set = await scenario.OpenAsync();
        var added = await scenario.RunAsync(directory => directory.AddOperationAsync(set.Id, Operation(OperationKind.Create, ItemKind.Media, asset, null, 1)));

        Assert.Equal(ChangeSetErrorCodes.ItemNotFound, Assert.Single(publish.Errors).Code);
        Assert.Contains("restore it first", publish.Errors[0].Message);
        Assert.Null(await PublishedMediaVersionAsync(scenario, asset));
        Assert.Equal(ChangeSetErrorCodes.ItemNotFound, Assert.Single(added.Errors).Code);

        Assert.True((await LibraryAsync(scenario, library => library.RestoreAsync(asset))).IsSuccess);
        Assert.True((await scenario.RunAsync(directory => directory.PublishAsync(approved.Id))).IsSuccess);
        Assert.Equal(1, await PublishedMediaVersionAsync(scenario, asset));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_agent_deletes_and_restores_media_only_with_the_delete_operation(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var asset = await UploadAsync(scenario, Metadata(null));
        await scenario.UpdateAgentAsync(row => row.Operations = [AgentOperation.Read, AgentOperation.Create, AgentOperation.Update]);
        scenario.Initiator = Agent;

        var refused = await LibraryAsync(scenario, library => library.DeleteAsync(asset, 1));
        await scenario.UpdateAgentAsync(row => row.Operations = [AgentOperation.Read, AgentOperation.Delete]);
        var deleted = await LibraryAsync(scenario, library => library.DeleteAsync(asset, 1));
        await scenario.UpdateAgentAsync(row => row.Operations = [AgentOperation.Read]);
        var refusedRestore = await LibraryAsync(scenario, library => library.RestoreAsync(asset));

        Assert.Equal(AgentPermissionErrorCodes.ScopeDenied, Assert.Single(refused.Errors).Code);
        Assert.True(deleted.IsSuccess);
        Assert.Equal(AgentPermissionErrorCodes.ScopeDenied, Assert.Single(refusedRestore.Errors).Code);
    }

    private static async Task<T> LibraryAsync<T>(ChangeSetScenario scenario, Func<IMediaLibrary, Task<T>> action)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IMediaLibrary>());
    }

    private static AssetMetadata Metadata(string? altText)
    {
        return AssetMetadata.Create("drill.jpg", "products", ["product-shot"], altText, null).Value;
    }

    private static async Task<Guid> UploadAsync(ChangeSetScenario scenario, AssetMetadata metadata)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        var asset = await scope.ServiceProvider.GetRequiredService<IMediaLibrary>().UploadAsync(new NewAsset(new MemoryStream(MediaFiles.Jpeg()), "image/jpeg", metadata));
        return asset.Value.Id;
    }

    private static async Task UpdateAsync(ChangeSetScenario scenario, Guid asset, int baseVersion, AssetMetadata metadata)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        Assert.True((await scope.ServiceProvider.GetRequiredService<IMediaLibrary>().UpdateAsync(asset, baseVersion, metadata)).IsSuccess);
    }

    private static Task<int?> PublishedMediaVersionAsync(ChangeSetScenario scenario, Guid asset)
    {
        return scenario.QueryAsync(context => context.MediaAssets.AsNoTracking().Where(row => row.Id == asset).Select(row => row.PublishedVersion).SingleAsync());
    }
}
