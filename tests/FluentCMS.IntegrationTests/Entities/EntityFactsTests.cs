using FluentCMS.Core.Agents;
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

public sealed class EntityFactsTests
{
    private const string Sourced = """{"kind": "url", "reference": "https://example.com/catalog"}""";

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_fact_saved_without_a_source_is_flagged_for_its_version(string provider)
    {
        await using var scenario = await CreateAsync(provider);
        var product = await ProductAsync(scenario, nameSource: "null");

        var first = (await FactsAsync(scenario, facts => facts.GetAsync(product))).Value;

        Assert.Equal(("product", 1), (first.Type, first.Version));
        Assert.Equal([("name", true), ("price", false)], first.Facts.Select(fact => (fact.Field, fact.IsUnsourced)));
        Assert.Equal(new DateTimeOffset(2026, 12, 31, 22, 59, 59, TimeSpan.Zero), first.Facts[1].ValidUntil);
        Assert.Equal("url", first.Facts[1].SourceKind);

        await scenario.UpdateAsync(product, 1, ProductFields(nameSource: Sourced, price: "109"));
        var second = (await FactsAsync(scenario, facts => facts.GetAsync(product))).Value;
        var stillFirst = (await FactsAsync(scenario, facts => facts.GetAsync(product, 1))).Value;

        Assert.Equal(2, second.Version);
        Assert.DoesNotContain(second.Facts, fact => fact.IsUnsourced);
        Assert.True(stillFirst.Facts[0].IsUnsourced);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task The_sites_unsourced_facts_are_listed_by_the_versions_that_hold_them(string provider)
    {
        await using var scenario = await CreateAsync(provider);
        var product = await ProductAsync(scenario, nameSource: "null");
        var category = (await scenario.CreateAsync("category", $$"""{"name": {{Fact("\"Tools\"")}}}""")).Value.Id;
        await scenario.Schema.QueryAsync(context => context.Entries
            .Where(row => row.Id == product)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PublishedVersion, 1)));
        await scenario.UpdateAsync(product, 1, ProductFields(nameSource: Sourced, price: "109"));
        await scenario.CreateAsync("blog-post", """{"title": "No facts here"}""");

        var all = (await FactsAsync(scenario, facts => facts.UnsourcedAsync())).Value;
        var products = (await FactsAsync(scenario, facts => facts.UnsourcedAsync("product"))).Value;

        Assert.Equal(
            new[]
            {
                new FactFlag(category, "category", "name", 1, IsLatest: true, IsPublished: false),
                new FactFlag(product, "product", "name", 1, IsLatest: false, IsPublished: true),
            },
            all);
        Assert.Equal([new FactFlag(product, "product", "name", 1, IsLatest: false, IsPublished: true)], products);
        Assert.Equal(SchemaErrorCodes.ContentTypeNotFound, Assert.Single((await FactsAsync(scenario, facts => facts.UnsourcedAsync("news"))).Errors).Code);
        Assert.Equal(EntityErrorCodes.TypeNotEntity, Assert.Single((await FactsAsync(scenario, facts => facts.UnsourcedAsync("blog-post"))).Errors).Code);

        await scenario.RunAsync(entries => entries.DeleteAsync(category, 1));
        Assert.DoesNotContain((await FactsAsync(scenario, facts => facts.UnsourcedAsync())).Value, flag => flag.EntityId == category);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Routing_gets_the_unsourced_facts_a_change_set_proposes(string provider)
    {
        await using var scenario = await CreateAsync(provider);
        var created = await ProductAsync(scenario, nameSource: "null");
        var updated = await ProductAsync(scenario, nameSource: Sourced);
        await scenario.UpdateAsync(updated, 1, ProductFields(nameSource: Sourced, price: "null"));
        var retired = await ProductAsync(scenario, nameSource: "null");
        await scenario.Schema.QueryAsync(context => context.Entries
            .Where(row => row.Id == retired || row.Id == updated)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PublishedVersion, 1)));
        var page = (await scenario.CreateAsync("blog-post", """{"title": "Launch"}""")).Value.Id;

        var setId = await ChangeSetAsync(
            scenario,
            Operation(OperationKind.Create, ItemKind.Entity, created, null, 1),
            Operation(OperationKind.Update, ItemKind.Entity, updated, 1, 2),
            Operation(OperationKind.Delete, ItemKind.Entity, retired, 1, null),
            Operation(OperationKind.Create, ItemKind.Entry, page, null, 1));

        var flags = (await FactsAsync(scenario, facts => facts.UnsourcedInChangeSetAsync(setId))).Value;

        Assert.Equal(
            new[]
            {
                new FactFlag(created, "product", "name", 1, IsLatest: true, IsPublished: false),
                new FactFlag(updated, "product", "price", 2, IsLatest: true, IsPublished: false),
            }.OrderBy(flag => flag.EntityId),
            flags);
        Assert.Equal(ChangeSetErrorCodes.ChangeSetNotFound, Assert.Single((await FactsAsync(scenario, facts => facts.UnsourcedInChangeSetAsync(Guid.CreateVersion7()))).Errors).Code);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Plain_entries_have_no_facts_and_unknown_ones_fail(string provider)
    {
        await using var scenario = await CreateAsync(provider);
        var post = (await scenario.CreateAsync("blog-post", """{"title": "News"}""")).Value.Id;
        var product = await ProductAsync(scenario, nameSource: "null");

        Assert.Equal(ContentErrorCodes.EntryNotEntity, Assert.Single((await FactsAsync(scenario, facts => facts.GetAsync(post))).Errors).Code);
        Assert.Equal(ContentErrorCodes.EntryNotFound, Assert.Single((await FactsAsync(scenario, facts => facts.GetAsync(Guid.CreateVersion7()))).Errors).Code);
        Assert.Equal(ContentErrorCodes.VersionNotFound, Assert.Single((await FactsAsync(scenario, facts => facts.GetAsync(product, 2))).Errors).Code);
        Assert.Equal(
            [product],
            await scenario.Schema.QueryAsync(context => context.EntityFacts.Select(row => row.EntryId).Distinct().ToListAsync()));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_agent_sees_facts_only_within_its_scope(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);
        scenario.Initiator = ChangeSetScenario.Amir;
        await using (var scope = scenario.Services.CreateAsyncScope())
        {
            var types = scope.ServiceProvider.GetRequiredService<IContentTypeDirectory>();
            var entries = scope.ServiceProvider.GetRequiredService<IEntryDirectory>();
            foreach (var (slug, isEntity, fields) in SchemaScenario.ReferenceModel)
            {
                Assert.True((await types.CreateAsync(new NewContentType(slug, slug, new ContentTypeDefinition(fields, isEntity)), TestChangeSetTargets.ContentModel)).IsSuccess);
            }

            Assert.True((await entries.CreateAsync(new NewEntry("category", Json($$"""{"name": {{Fact("\"Tools\"")}}}""")))).IsSuccess);
            Assert.True((await entries.CreateAsync(new NewEntry("product", Json(ProductFields("null", "99"))))).IsSuccess);
        }

        await scenario.UpdateAgentAsync(row => row.Types = ["product"]);
        scenario.Initiator = ChangeSetScenario.Agent;

        await using var agentScope = scenario.Services.CreateAsyncScope();
        var facts = agentScope.ServiceProvider.GetRequiredService<IEntityFacts>();
        var visible = (await facts.UnsourcedAsync()).Value;
        var refused = await facts.UnsourcedAsync("category");

        Assert.All(visible, flag => Assert.Equal("product", flag.Type));
        Assert.Single(visible);
        Assert.Equal(AgentPermissionErrorCodes.ScopeDenied, Assert.Single(refused.Errors).Code);
    }

    private static async Task<EntryScenario> CreateAsync(string provider)
    {
        var scenario = await EntryScenario.CreateAsync(provider);
        await scenario.Schema.CreateReferenceModelAsync();
        return scenario;
    }

    private static async Task<Guid> ProductAsync(EntryScenario scenario, string nameSource)
    {
        var created = await scenario.CreateAsync("product", ProductFields(nameSource, "99"));
        Assert.True(created.IsSuccess, string.Join("; ", created.Errors.Select(error => error.Message)));
        return created.Value.Id;
    }

    // The name's source as given, and a price whose source is given unless it is "null".
    private static string ProductFields(string nameSource, string price)
    {
        var priceSource = price == "null" ? "null" : Sourced;
        var priceValue = price == "null" ? "1" : price;

        return $$"""
            {
              "name": {"value": "Drill", "source": {{nameSource}}, "owner": null, "validFrom": null, "validUntil": null, "lastVerified": null},
              "price": {"value": {{priceValue}}, "source": {{priceSource}}, "owner": null, "validFrom": null, "validUntil": "2026-12-31T23:59:59+01:00", "lastVerified": null}
            }
            """;
    }

    private static async Task<Guid> ChangeSetAsync(EntryScenario scenario, params ChangeOperation[] operations)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        var directory = scope.ServiceProvider.GetRequiredService<IChangeSetDirectory>();
        var set = (await directory.OpenAsync(new NewChangeSet("Catalog sync.", []))).Value;
        foreach (var operation in operations)
        {
            var added = await directory.AddOperationAsync(set.Id, operation);
            Assert.True(added.IsSuccess, string.Join("; ", added.Errors.Select(error => error.Message)));
        }

        return set.Id;
    }

    private static ChangeOperation Operation(OperationKind kind, ItemKind itemKind, Guid item, int? baseVersion, int? proposedVersion)
    {
        return ChangeOperation.Create(kind, itemKind, item, baseVersion, proposedVersion).Value;
    }

    private static async Task<Result<T>> FactsAsync<T>(EntryScenario scenario, Func<IEntityFacts, Task<Result<T>>> read)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<IEntityFacts>());
    }
}
