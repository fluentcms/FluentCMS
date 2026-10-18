using FluentCMS.Core.Content;
using FluentCMS.Core.Results;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FluentCMS.IntegrationTests.Content.EntryScenario;

namespace FluentCMS.IntegrationTests.Content;

public sealed class SoftDeleteTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_deleted_page_is_gone_from_every_read_and_from_where_used(string provider)
    {
        await using var scenario = await CreateAsync(provider, new ManualClock(Start));
        var product = await CreateProductAsync(scenario);
        var page = (await scenario.CreateAsync("product-page", $$"""{"price": "{{product}}", "headline": "Widget"}""")).Value.Id;

        Assert.True((await scenario.RunAsync(directory => directory.DeleteAsync(page, 1))).IsSuccess);

        Assert.Equal(ContentErrorCodes.EntryNotFound, Assert.Single((await scenario.RunAsync(directory => directory.GetAsync(page))).Errors).Code);
        Assert.Equal(ContentErrorCodes.EntryNotFound, Assert.Single((await scenario.RunAsync(directory => directory.GetVersionAsync(page, 1))).Errors).Code);
        Assert.Equal(ContentErrorCodes.EntryNotFound, Assert.Single((await scenario.RunAsync(directory => directory.HistoryAsync(page))).Errors).Code);
        Assert.Equal(ContentErrorCodes.EntryNotFound, Assert.Single((await scenario.UpdateAsync(page, 1, """{"headline": "Again"}""")).Errors).Code);
        Assert.Equal(ContentErrorCodes.EntryNotFound, Assert.Single((await ResolveAsync(scenario, page)).Errors).Code);
        Assert.Empty((await scenario.RunAsync(directory => directory.ListAsync("product-page"))).Value);
        Assert.Empty((await WhereUsedAsync(scenario, whereUsed => whereUsed.ForEntityFieldAsync(product, "price"))).Value);

        var deleted = Assert.Single(await scenario.EntryEventsAsync(), row => row.Operation == "entry.deleted");
        var version = Assert.Single(await scenario.VersionRowsAsync(), row => row.EntryId == page);
        Assert.Equal(page, deleted.SubjectId);
        Assert.Equal(version.Id, deleted.PreviousVersionId);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_deleted_entity_cannot_be_referenced_and_resolves_to_null(string provider)
    {
        await using var scenario = await CreateAsync(provider, new ManualClock(Start));
        var product = await CreateProductAsync(scenario);
        await scenario.Schema.QueryAsync(context => context.Entries
            .Where(row => row.Id == product)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PublishedVersion, 1)));
        var page = (await scenario.CreateAsync("product-page", $$"""{"price": "{{product}}", "headline": "Widget"}""")).Value.Id;

        await scenario.RunAsync(directory => directory.DeleteAsync(product, 1));

        var resolved = (await ResolveAsync(scenario, page)).Value;
        AssertSameFields("""{"price": null, "headline": "Widget"}""", resolved.Fields);
        Assert.Equal([new UnresolvedReference("/price", product, "price", UnresolvedReason.Deleted)], resolved.Unresolved);

        var reference = await scenario.CreateAsync("product-page", $$"""{"price": "{{product}}"}""");
        Assert.Equal(ContentErrorCodes.ReferenceNotFound, Assert.Single(reference.Errors).Code);
        Assert.Equal(ContentErrorCodes.EntryNotFound, Assert.Single((await WhereUsedAsync(scenario, whereUsed => whereUsed.ForEntryAsync(product))).Errors).Code);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Restore_within_the_window_brings_the_entry_back_with_its_history(string provider)
    {
        var clock = new ManualClock(Start);
        await using var scenario = await CreateAsync(provider, clock);
        var product = await CreateProductAsync(scenario);
        var page = (await scenario.CreateAsync("product-page", $$"""{"price": "{{product}}", "headline": "One"}""")).Value.Id;
        await scenario.UpdateAsync(page, 1, $$"""{"price": "{{product}}", "headline": "Two"}""");
        await scenario.RunAsync(directory => directory.DeleteAsync(page, 2));

        clock.Now = Start.AddDays(30).AddMinutes(-1);
        var restored = (await scenario.RunAsync(directory => directory.RestoreAsync(page))).Value;

        Assert.Equal(2, restored.LatestVersion);
        AssertSameFields($$"""{"price": "{{product}}", "headline": "Two"}""", restored.Latest.Fields);
        Assert.Equal([1, 2], (await scenario.RunAsync(directory => directory.HistoryAsync(page))).Value.Select(version => version.Version));
        Assert.Equal(page, Assert.Single((await WhereUsedAsync(scenario, whereUsed => whereUsed.ForEntityFieldAsync(product, "price"))).Value).EntryId);
        Assert.Empty(await scenario.RunAsync(directory => directory.ListDeletedAsync()));
        Assert.Equal(page, Assert.Single(await scenario.EntryEventsAsync(), row => row.Operation == "entry.restored").SubjectId);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task After_the_window_restore_fails_and_changes_nothing(string provider)
    {
        var clock = new ManualClock(Start);
        await using var scenario = await CreateAsync(provider, clock);
        var post = (await scenario.CreateAsync("blog-post", """{"title": "Old news"}""")).Value.Id;
        await scenario.RunAsync(directory => directory.DeleteAsync(post, 1));
        var events = (await scenario.EntryEventsAsync()).Count;

        clock.Now = Start.AddDays(30);
        var restore = await scenario.RunAsync(directory => directory.RestoreAsync(post));

        Assert.Equal(ContentErrorCodes.RetentionExpired, Assert.Single(restore.Errors).Code);
        Assert.Equal(ContentErrorCodes.EntryNotFound, Assert.Single((await scenario.RunAsync(directory => directory.GetAsync(post))).Errors).Code);
        Assert.Equal(events, (await scenario.EntryEventsAsync()).Count);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Deletes_and_restores_that_do_not_fit_the_entrys_state_fail(string provider)
    {
        await using var scenario = await CreateAsync(provider, new ManualClock(Start));
        var post = (await scenario.CreateAsync("blog-post", """{"title": "News"}""")).Value.Id;
        await scenario.UpdateAsync(post, 1, """{"title": "More news"}""");

        var stale = await scenario.RunAsync(directory => directory.DeleteAsync(post, 1));
        var notDeleted = await scenario.RunAsync(directory => directory.RestoreAsync(post));
        var unknownDelete = await scenario.RunAsync(directory => directory.DeleteAsync(Guid.CreateVersion7(), 1));
        var unknownRestore = await scenario.RunAsync(directory => directory.RestoreAsync(Guid.CreateVersion7()));
        Assert.True((await scenario.RunAsync(directory => directory.DeleteAsync(post, 2))).IsSuccess);
        var twice = await scenario.RunAsync(directory => directory.DeleteAsync(post, 2));

        Assert.Equal(ContentErrorCodes.VersionConflict, Assert.Single(stale.Errors).Code);
        Assert.Equal(ContentErrorCodes.EntryNotDeleted, Assert.Single(notDeleted.Errors).Code);
        Assert.Equal(ContentErrorCodes.EntryNotFound, Assert.Single(unknownDelete.Errors).Code);
        Assert.Equal(ContentErrorCodes.EntryNotFound, Assert.Single(unknownRestore.Errors).Code);
        Assert.Equal(ContentErrorCodes.EntryNotFound, Assert.Single(twice.Errors).Code);
        Assert.Single(await scenario.EntryEventsAsync(), row => row.Operation == "entry.deleted");
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task The_deleted_list_says_when_each_entry_was_deleted_and_until_when_it_can_be_restored(string provider)
    {
        var clock = new ManualClock(Start);
        await using var scenario = await CreateAsync(provider, clock);
        var first = (await scenario.CreateAsync("blog-post", """{"title": "First"}""")).Value.Id;
        var second = (await scenario.CreateAsync("blog-post", """{"title": "Second"}""")).Value.Id;
        await scenario.CreateAsync("blog-post", """{"title": "Kept"}""");

        await scenario.RunAsync(directory => directory.DeleteAsync(first, 1));
        clock.Now = Start.AddHours(1);
        await scenario.RunAsync(directory => directory.DeleteAsync(second, 1));

        Assert.Equal(
            [
                new DeletedEntry(second, "blog-post", 1, Start.AddHours(1), Start.AddHours(1).AddDays(30)),
                new DeletedEntry(first, "blog-post", 1, Start, Start.AddDays(30)),
            ],
            await scenario.RunAsync(directory => directory.ListDeletedAsync()));
    }

    private static async Task<EntryScenario> CreateAsync(string provider, ManualClock clock)
    {
        var scenario = await EntryScenario.CreateAsync(provider, services => services.AddSingleton<TimeProvider>(clock));
        await scenario.Schema.CreateReferenceModelAsync();
        return scenario;
    }

    private static async Task<Guid> CreateProductAsync(EntryScenario scenario)
    {
        var created = await scenario.CreateAsync("product", $$"""{"name": {{Fact("\"Widget\"")}}, "price": {{Fact("19.99")}}}""");
        Assert.True(created.IsSuccess, string.Join("; ", created.Errors.Select(error => error.Message)));
        return created.Value.Id;
    }

    private static async Task<Result<ResolvedEntry>> ResolveAsync(EntryScenario scenario, Guid entry)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IResolvedEntries>().ResolveAsync(entry);
    }

    private static async Task<Result<IReadOnlyList<EntryUsage>>> WhereUsedAsync(
        EntryScenario scenario,
        Func<IWhereUsed, Task<Result<IReadOnlyList<EntryUsage>>>> query)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<IWhereUsed>());
    }
}
