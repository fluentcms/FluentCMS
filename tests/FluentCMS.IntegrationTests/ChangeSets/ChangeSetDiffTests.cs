using FluentCMS.Core.ChangeSets;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using static FluentCMS.IntegrationTests.ChangeSets.ChangeSetScenario;

namespace FluentCMS.IntegrationTests.ChangeSets;

public sealed class ChangeSetDiffTests
{
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_stored_set_diffs_each_operation_from_its_stored_versions_in_order(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);
        var product = await scenario.ContentTypeAsync("product", revised: true);
        var category = await scenario.ContentTypeAsync("category");
        var changeSet = await scenario.OpenAsync(Update(product), Create(category));

        var diff = (await scenario.RunAsync(directory => directory.DiffAsync(changeSet.Id))).Value;

        Assert.Equal(changeSet.Id, diff.ChangeSetId);
        Assert.Equal([Update(product), Create(category)], diff.Items.Select(item => item.Operation));

        var update = diff.Items[0];
        Assert.Empty(update.Properties);
        var summary = Assert.Single(update.Fields);
        Assert.Equal(("summary", ValueChange.Added), (summary.Name, summary.Change));
        Assert.Equal("text", summary.After!.Value.GetProperty("type").GetString());

        var create = diff.Items[1];
        var entity = Assert.Single(create.Properties);
        Assert.Equal((ItemDiff.EntityProperty, ValueChange.Added, false), (entity.Name, entity.Change, entity.After!.Value.GetBoolean()));
        var title = Assert.Single(create.Fields);
        Assert.Equal(("title", ValueChange.Added), (title.Name, title.Change));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_diff_is_a_read_that_appends_no_event(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);
        var product = await scenario.ContentTypeAsync("product", revised: true);
        var changeSet = await scenario.OpenAsync(Update(product));
        var events = await EventCountAsync(scenario);

        Assert.True((await scenario.RunAsync(directory => directory.DiffAsync(changeSet.Id))).IsSuccess);
        var missing = await scenario.RunAsync(directory => directory.DiffAsync(Guid.NewGuid()));

        Assert.Equal(ChangeSetErrorCodes.ChangeSetNotFound, Assert.Single(missing.Errors).Code);
        Assert.Equal(events, await EventCountAsync(scenario));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_empty_set_has_an_empty_diff(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);
        var changeSet = await scenario.OpenAsync();

        var diff = (await scenario.RunAsync(directory => directory.DiffAsync(changeSet.Id))).Value;

        Assert.Equal(changeSet.Id, diff.ChangeSetId);
        Assert.Empty(diff.Items);
    }

    private static Task<int> EventCountAsync(ChangeSetScenario scenario)
    {
        return scenario.QueryAsync((FluentCmsDbContext context) => context.AuditEvents.CountAsync());
    }
}
