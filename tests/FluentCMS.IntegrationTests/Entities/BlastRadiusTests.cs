using System.Diagnostics;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Content;
using FluentCMS.Core.Entities;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using FluentCMS.Infrastructure.Persistence;
using FluentCMS.IntegrationTests.ChangeSets;
using FluentCMS.IntegrationTests.Content;
using FluentCMS.IntegrationTests.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FluentCMS.IntegrationTests.Content.EntryScenario;

namespace FluentCMS.IntegrationTests.Entities;

public sealed class BlastRadiusTests
{
    private const string Catalog = """{"kind": "file", "reference": "exports/catalog.csv"}""";
    private const string Supplier = """{"kind": "url", "reference": "https://example.com/supplier"}""";

    // S1 in the F-ENT acceptance: one set changes prices, and every live page showing one of them comes with it.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Every_published_page_showing_a_changed_price_is_in_the_blast_radius_with_both_prices(string provider)
    {
        await using var scenario = await CreateAsync(provider);
        var (drill, saw, sander) = (await ProductAsync(scenario, "99"), await ProductAsync(scenario, "49"), await ProductAsync(scenario, "79"));
        var drillPage = await PageAsync(scenario, drill);
        var sawPage = await PageAsync(scenario, saw);
        var sanderPage = await PageAsync(scenario, sander);
        var comparison = await EntryAsync(scenario, "comparison-page", $$"""{"prices": ["{{drill}}", "{{sander}}"]}""");
        var draft = await EntryAsync(scenario, "product-page", $$"""{"price": "{{drill}}", "headline": "Draft"}""");
        var deleted = await PageAsync(scenario, drill);
        await PublishAsync(scenario, drill, saw, sander, drillPage, sawPage, sanderPage, comparison);
        await scenario.RunAsync(entries => entries.DeleteAsync(deleted, 1));

        await scenario.UpdateAsync(drill, 1, ProductFields("109", Catalog));
        await scenario.UpdateAsync(saw, 1, ProductFields("59", Catalog));
        await scenario.UpdateAsync(sander, 1, ProductFields("79", Supplier));
        var set = await ChangeSetAsync(scenario, Update(drill), Update(saw), Update(sander));

        var radius = (await RadiusAsync(scenario, set)).Value;

        Assert.Equal(set, radius.ChangeSetId);
        Assert.Equal(
            new[] { (comparison, "comparison-page"), (drillPage, "product-page"), (sawPage, "product-page") }
                .OrderBy(entry => entry.Item2, StringComparer.Ordinal)
                .ThenBy(entry => entry.Item1),
            radius.Entries.Select(entry => (entry.EntryId, entry.ContentType)));
        Assert.DoesNotContain(radius.Entries, entry => entry.EntryId == draft || entry.EntryId == deleted || entry.EntryId == sanderPage);

        AssertPrice(radius, drillPage, "/price", 99, 109);
        AssertPrice(radius, sawPage, "/price", 49, 59);
        AssertPrice(radius, comparison, "/prices/0", 99, 109);
        Assert.All(radius.Entries, entry => Assert.Equal(1, entry.Version));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_created_entity_adds_its_values_and_a_deleted_one_removes_them(string provider)
    {
        await using var scenario = await CreateAsync(provider);
        var drill = await ProductAsync(scenario, "99");
        var newcomer = await ProductAsync(scenario, "19");
        var drillPage = await PageAsync(scenario, drill);
        var teaser = await PageAsync(scenario, newcomer);
        await PublishAsync(scenario, drill, drillPage, teaser);

        var set = await ChangeSetAsync(
            scenario,
            Operation(OperationKind.Create, newcomer, null, 1),
            Operation(OperationKind.Delete, drill, 1, null));
        var radius = (await RadiusAsync(scenario, set)).Value;

        var added = Assert.Single(Assert.Single(radius.Entries, entry => entry.EntryId == teaser).Fields);
        var removed = Assert.Single(Assert.Single(radius.Entries, entry => entry.EntryId == drillPage).Fields);
        Assert.Equal(("/price", ValueChange.Added, 19m), (added.Name, added.Change, added.After!.Value.GetDecimal()));
        Assert.Equal(("/price", ValueChange.Removed, 99m), (removed.Name, removed.Change, removed.Before!.Value.GetDecimal()));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Two_hundred_pages_come_back_within_the_budget(string provider)
    {
        await using var scenario = await CreateAsync(provider);
        var drill = await ProductAsync(scenario, "99");
        var pages = new List<Guid>();
        for (var page = 0; page < 200; page++)
        {
            pages.Add(await PageAsync(scenario, drill));
        }

        await PublishAsync(scenario, [drill, .. pages]);
        await scenario.UpdateAsync(drill, 1, ProductFields("109", Catalog));
        var set = await ChangeSetAsync(scenario, Update(drill));

        var clock = Stopwatch.StartNew();
        var radius = (await RadiusAsync(scenario, set)).Value;
        clock.Stop();

        Assert.Equal(pages.Order(), radius.Entries.Select(entry => entry.EntryId));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"The blast radius took {clock.Elapsed}.");
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_unknown_set_fails_and_a_set_without_entity_changes_has_no_radius(string provider)
    {
        await using var scenario = await CreateAsync(provider);
        var post = await EntryAsync(scenario, "blog-post", """{"title": "News"}""");
        var set = await ChangeSetAsync(scenario, ChangeOperation.Create(OperationKind.Create, ItemKind.Entry, post, null, 1).Value);

        Assert.Empty((await RadiusAsync(scenario, set)).Value.Entries);
        Assert.Equal(ChangeSetErrorCodes.ChangeSetNotFound, Assert.Single((await RadiusAsync(scenario, Guid.CreateVersion7())).Errors).Code);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_agent_sees_only_affected_pages_of_types_in_its_scope(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);
        scenario.Initiator = ChangeSetScenario.Amir;
        Guid set;
        await using (var scope = scenario.Services.CreateAsyncScope())
        {
            var types = scope.ServiceProvider.GetRequiredService<IContentTypeDirectory>();
            var entries = scope.ServiceProvider.GetRequiredService<IEntryDirectory>();
            foreach (var (slug, isEntity, fields) in SchemaScenario.ReferenceModel)
            {
                Assert.True((await types.CreateAsync(new NewContentType(slug, slug, new ContentTypeDefinition(fields, isEntity)), TestChangeSetTargets.ContentModel)).IsSuccess);
            }

            var drill = (await entries.CreateAsync(new NewEntry("product", Json(ProductFields("99", Catalog))))).Value.Id;
            var page = (await entries.CreateAsync(new NewEntry("product-page", Json($$"""{"price": "{{drill}}"}""")))).Value.Id;
            await scenario.QueryAsync(context => context.Entries
                .Where(row => row.Id == drill || row.Id == page)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PublishedVersion, 1)));
            await entries.UpdateAsync(drill, 1, Json(ProductFields("109", Catalog)));

            var changeSets = scope.ServiceProvider.GetRequiredService<IChangeSetDirectory>();
            set = (await changeSets.OpenAsync(new NewChangeSet("Prices.", []))).Value.Id;
            Assert.True((await changeSets.AddOperationAsync(set, Update(drill))).IsSuccess);
        }

        Assert.Single((await RadiusAsync(scenario.Services, set)).Value.Entries);

        await scenario.UpdateAgentAsync(row => row.Types = ["product"]);
        scenario.Initiator = ChangeSetScenario.Agent;

        Assert.Empty((await RadiusAsync(scenario.Services, set)).Value.Entries);
    }

    private static async Task<EntryScenario> CreateAsync(string provider)
    {
        var scenario = await EntryScenario.CreateAsync(provider);
        await scenario.Schema.CreateReferenceModelAsync();
        return scenario;
    }

    private static string ProductFields(string price, string source)
    {
        return $$"""
            {
              "name": {{Fact("\"Drill\"")}},
              "price": {"value": {{price}}, "source": {{source}}, "owner": null, "validFrom": null, "validUntil": null, "lastVerified": null}
            }
            """;
    }

    private static Task<Guid> ProductAsync(EntryScenario scenario, string price)
    {
        return EntryAsync(scenario, "product", ProductFields(price, Catalog));
    }

    private static Task<Guid> PageAsync(EntryScenario scenario, Guid product)
    {
        return EntryAsync(scenario, "product-page", $$"""{"product": "{{product}}", "price": "{{product}}", "headline": "Buy it"}""");
    }

    private static async Task<Guid> EntryAsync(EntryScenario scenario, string contentType, string fields)
    {
        var created = await scenario.CreateAsync(contentType, fields);
        Assert.True(created.IsSuccess, string.Join("; ", created.Errors.Select(error => error.Message)));
        return created.Value.Id;
    }

    // Publish moves the pointer through change sets (#2519); these tests only need pages that are live.
    private static Task<int> PublishAsync(EntryScenario scenario, params Guid[] entries)
    {
        return scenario.Schema.QueryAsync(context => context.Entries
            .Where(row => entries.Contains(row.Id))
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PublishedVersion, 1)));
    }

    private static async Task<Guid> ChangeSetAsync(EntryScenario scenario, params ChangeOperation[] operations)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        var directory = scope.ServiceProvider.GetRequiredService<IChangeSetDirectory>();
        var set = (await directory.OpenAsync(new NewChangeSet("Sync prices from the catalog export.", ["exports/catalog.csv"]))).Value;
        foreach (var operation in operations)
        {
            var added = await directory.AddOperationAsync(set.Id, operation);
            Assert.True(added.IsSuccess, string.Join("; ", added.Errors.Select(error => error.Message)));
        }

        return set.Id;
    }

    private static ChangeOperation Update(Guid entity)
    {
        return Operation(OperationKind.Update, entity, 1, 2);
    }

    private static ChangeOperation Operation(OperationKind kind, Guid entity, int? baseVersion, int? proposedVersion)
    {
        return ChangeOperation.Create(kind, ItemKind.Entity, entity, baseVersion, proposedVersion).Value;
    }

    private static Task<Result<BlastRadius>> RadiusAsync(EntryScenario scenario, Guid set)
    {
        return RadiusAsync(scenario.Services, set);
    }

    private static async Task<Result<BlastRadius>> RadiusAsync(IServiceProvider services, Guid set)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IBlastRadius>().ForChangeSetAsync(set);
    }

    private static void AssertPrice(BlastRadius radius, Guid entry, string path, decimal before, decimal after)
    {
        var diff = Assert.Single(Assert.Single(radius.Entries, affected => affected.EntryId == entry).Fields);
        Assert.Equal((path, ValueChange.Changed), (diff.Name, diff.Change));
        Assert.Equal(before, diff.Before!.Value.GetDecimal());
        Assert.Equal(after, diff.After!.Value.GetDecimal());
    }
}
