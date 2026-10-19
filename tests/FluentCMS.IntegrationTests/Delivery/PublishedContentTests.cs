using FluentCMS.Core.Agents;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Content;
using FluentCMS.Core.Delivery;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using FluentCMS.Infrastructure.Persistence;
using FluentCMS.IntegrationTests.ChangeSets;
using FluentCMS.IntegrationTests.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FluentCMS.IntegrationTests.Content.EntryScenario;

namespace FluentCMS.IntegrationTests.Delivery;

public sealed class PublishedContentTests
{
    // The F-API acceptance: a frontend reads a product page with the product's price as published, whatever its drafts say.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_page_reads_as_published_with_its_price_resolved_to_the_published_value(string provider)
    {
        await using var scenario = await CreateAsync(provider);
        var product = await ProductAsync(scenario, "99");
        await PublishAsync(scenario, product, 1);
        await scenario.UpdateAsync(product, 1, ProductFields("109"));
        var page = await PageAsync(scenario, product, "A sturdy drill");
        await PublishAsync(scenario, page, 1);
        await scenario.UpdateAsync(page, 1, $$"""{"product": "{{product}}", "price": "{{product}}", "headline": "Draft headline"}""");

        var read = (await ContentAsync(scenario, content => content.GetAsync(page))).Value;
        var entity = (await ContentAsync(scenario, content => content.GetAsync(product))).Value;

        Assert.Equal((page, "product-page", 1), (read.Id, read.ContentType, read.Version));
        AssertSameFields($$"""{"product": "{{product}}", "price": 99, "headline": "A sturdy drill"}""", read.Fields);
        Assert.Empty(read.Unresolved);
        Assert.Equal(("product", 1), (entity.ContentType, entity.Version));
        Assert.Equal(99, entity.Fields.GetProperty("price").GetProperty("value").GetInt32());
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Drafts_unpublished_and_deleted_entries_are_neither_listed_nor_read(string provider)
    {
        await using var scenario = await CreateAsync(provider);
        var product = await ProductAsync(scenario, "99");
        var live = await PageAsync(scenario, product, "Live");
        var draft = await PageAsync(scenario, product, "Draft");
        var deleted = await PageAsync(scenario, product, "Deleted");
        await PublishAsync(scenario, live, 1);
        await PublishAsync(scenario, deleted, 1);
        await scenario.RunAsync(entries => entries.DeleteAsync(deleted, 1));

        var archived = await PageAsync(scenario, product, "Archived");
        await scenario.Schema.QueryAsync(context => context.Entries
            .Where(row => row.Id == archived)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PublishedVersion, (int?)null).SetProperty(row => row.State, EntryState.Archived)));

        var listed = (await ContentAsync(scenario, content => content.ListAsync(new PublishedQuery("product-page")))).Value;
        var products = (await ContentAsync(scenario, content => content.ListAsync(new PublishedQuery("product")))).Value;

        Assert.Equal([live], listed.Items.Select(item => item.Id));
        Assert.Null(listed.Next);
        Assert.Empty(products.Items);
        Assert.Equal("/price", Assert.Single(listed.Items[0].Unresolved).Path);
        foreach (var hidden in new[] { draft, deleted, archived, product, Guid.CreateVersion7() })
        {
            Assert.Equal(ContentErrorCodes.EntryNotFound, Code(await ContentAsync(scenario, content => content.GetAsync(hidden))));
        }
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Pages_follow_the_order_of_creation_and_end_without_a_next_cursor(string provider)
    {
        await using var scenario = await CreateAsync(provider);
        var product = await ProductAsync(scenario, "99");
        var pages = new List<Guid>();
        for (var number = 1; number <= 5; number++)
        {
            pages.Add(await PageAsync(scenario, product, $"Page {number}"));
            await PageAsync(scenario, product, $"Draft {number}");
            await PublishAsync(scenario, pages[^1], 1);
        }

        var read = new List<Guid>();
        var cursors = new List<Guid?>();
        Guid? after = null;
        do
        {
            var page = (await ContentAsync(scenario, content => content.ListAsync(new PublishedQuery("product-page", 2, after)))).Value;
            read.AddRange(page.Items.Select(item => item.Id));
            cursors.Add(page.Next);
            after = page.Next;
        }
        while (after is not null);

        Assert.Equal(pages, read);
        Assert.Equal([pages[1], pages[3], null], cursors);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task References_keeps_the_entries_whose_published_version_points_at_the_target(string provider)
    {
        await using var scenario = await CreateAsync(provider);
        var (drill, saw) = (await ProductAsync(scenario, "99"), await ProductAsync(scenario, "49"));
        var drillPage = await PageAsync(scenario, drill, "Drill");
        var sawPage = await PageAsync(scenario, saw, "Saw");
        var movedPage = await PageAsync(scenario, saw, "Was a saw");
        await PublishAsync(scenario, drillPage, 1);
        await PublishAsync(scenario, sawPage, 1);
        await PublishAsync(scenario, movedPage, 1);
        await scenario.UpdateAsync(movedPage, 1, $$"""{"product": "{{drill}}", "price": "{{drill}}", "headline": "Now a drill"}""");

        var drillPages = (await ContentAsync(scenario, content => content.ListAsync(new PublishedQuery("product-page", References: drill)))).Value;
        var sawPages = (await ContentAsync(scenario, content => content.ListAsync(new PublishedQuery("product-page", References: saw)))).Value;

        Assert.Equal([drillPage], drillPages.Items.Select(item => item.Id));
        Assert.Equal([sawPage, movedPage], sawPages.Items.Select(item => item.Id));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_limit_out_of_range_or_an_unknown_type_fails(string provider)
    {
        await using var scenario = await CreateAsync(provider);

        Assert.Equal(DeliveryErrorCodes.LimitInvalid, Code(await ContentAsync(scenario, content => content.ListAsync(new PublishedQuery("product-page", 0)))));
        Assert.Equal(DeliveryErrorCodes.LimitInvalid, Code(await ContentAsync(scenario, content => content.ListAsync(new PublishedQuery("product-page", 101)))));
        Assert.Equal(SchemaErrorCodes.ContentTypeNotFound, Code(await ContentAsync(scenario, content => content.ListAsync(new PublishedQuery("news")))));
        Assert.Empty((await ContentAsync(scenario, content => content.ListAsync(new PublishedQuery("product-page", 100)))).Value.Items);
    }

    // An agent that reads through these services keeps to its scope (MCP-05).
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_agent_reads_only_published_content_of_types_it_may_read(string provider)
    {
        await using var scenario = await EntryChangeSetTests.ScenarioAsync(provider);
        var (page, product) = await EntryChangeSetTests.CatalogAsync(scenario);
        await scenario.PublishAsync(
            ChangeOperation.Create(OperationKind.Create, ItemKind.Entity, product, null, 1).Value,
            ChangeOperation.Create(OperationKind.Create, ItemKind.Entry, page, null, 1).Value);
        await scenario.UpdateAgentAsync(row => row.Types = ["product"]);
        scenario.Initiator = ChangeSetScenario.Agent;

        await using var scope = scenario.Services.CreateAsyncScope();
        var content = scope.ServiceProvider.GetRequiredService<IPublishedContent>();

        Assert.Equal(AgentPermissionErrorCodes.ScopeDenied, Code(await content.GetAsync(page)));
        Assert.Equal(AgentPermissionErrorCodes.ScopeDenied, Code(await content.ListAsync(new PublishedQuery("page"))));
        Assert.Equal([product], (await content.ListAsync(new PublishedQuery("product"))).Value.Items.Select(item => item.Id));
    }

    private static async Task<EntryScenario> CreateAsync(string provider)
    {
        var scenario = await EntryScenario.CreateAsync(provider);
        await scenario.Schema.CreateReferenceModelAsync();
        return scenario;
    }

    private static string ProductFields(string price)
    {
        return $$"""{"name": {{Fact("\"Drill\"")}}, "price": {{Fact(price)}}}""";
    }

    private static async Task<Guid> ProductAsync(EntryScenario scenario, string price)
    {
        var created = await scenario.CreateAsync("product", ProductFields(price));
        Assert.True(created.IsSuccess, string.Join("; ", created.Errors.Select(error => error.Message)));
        return created.Value.Id;
    }

    private static async Task<Guid> PageAsync(EntryScenario scenario, Guid product, string headline)
    {
        var created = await scenario.CreateAsync("product-page", $$"""{"product": "{{product}}", "price": "{{product}}", "headline": "{{headline}}"}""");
        Assert.True(created.IsSuccess, string.Join("; ", created.Errors.Select(error => error.Message)));
        return created.Value.Id;
    }

    // Publish moves the pointer through change sets (#2519); these tests only need published versions.
    private static Task<int> PublishAsync(EntryScenario scenario, Guid entry, int version)
    {
        return scenario.Schema.QueryAsync(context => context.Entries
            .Where(row => row.Id == entry)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PublishedVersion, version)));
    }

    private static string Code<T>(Result<T> result)
    {
        return Assert.Single(result.Errors).Code;
    }

    private static async Task<T> ContentAsync<T>(EntryScenario scenario, Func<IPublishedContent, Task<T>> read)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<IPublishedContent>());
    }
}
