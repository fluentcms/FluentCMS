using FluentCMS.Core.Agents;
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

public sealed class EntityRelationsTests
{
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_product_relates_to_its_category_and_specs_and_they_relate_back(string provider)
    {
        await using var scenario = await CreateAsync(provider);
        var catalog = await CatalogAsync(scenario);

        var product = (await RelationsAsync(scenario, catalog.Product)).Value;
        var category = (await RelationsAsync(scenario, catalog.Category)).Value;
        var spec = (await RelationsAsync(scenario, catalog.Width)).Value;

        Assert.Equal("product", product.Type);
        Assert.Equal(
            [
                Relation(catalog.Product, "product", "category", catalog.Category, "category"),
                .. new[] { catalog.Width, catalog.Height }.Order().Select(id => Relation(catalog.Product, "product", "specs", id, "spec")),
            ],
            product.Outgoing);
        Assert.Empty(product.Incoming);
        Assert.Equal([Relation(catalog.Product, "product", "category", catalog.Category, "category")], category.Incoming);
        Assert.Equal([Relation(catalog.Product, "product", "specs", catalog.Width, "spec")], spec.Incoming);
        Assert.Empty(spec.Outgoing);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Relations_survive_new_versions_and_belong_to_the_versions_that_hold_them(string provider)
    {
        await using var scenario = await CreateAsync(provider);
        var catalog = await CatalogAsync(scenario);
        await PublishAsync(scenario, catalog.Product, 1);

        await scenario.UpdateAsync(catalog.Category, 1, $$"""{"name": {{Fact("\"Power tools\"")}}}""");
        var renamed = (await RelationsAsync(scenario, catalog.Category)).Value;
        Assert.Equal([Relation(catalog.Product, "product", "category", catalog.Category, "category", latest: true, published: true)], renamed.Incoming);

        await scenario.UpdateAsync(catalog.Product, 1, $$"""{"name": {{Fact("\"Drill\"")}}, "price": {{Fact("99")}}, "specs": {{Fact($"[\"{catalog.Width}\"]")}}}""");
        var product = (await RelationsAsync(scenario, catalog.Product)).Value;

        Assert.Equal(
            [
                Relation(catalog.Product, "product", "category", catalog.Category, "category", latest: false, published: true),
                Relation(catalog.Product, "product", "specs", catalog.Height, "spec", latest: false, published: true),
                Relation(catalog.Product, "product", "specs", catalog.Width, "spec", latest: true, published: true),
            ],
            product.Outgoing.OrderBy(relation => relation.Field).ThenBy(relation => relation.ToId == catalog.Width));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Pages_are_uses_not_relations_and_deleted_entities_drop_out(string provider)
    {
        await using var scenario = await CreateAsync(provider);
        var catalog = await CatalogAsync(scenario);
        await scenario.CreateAsync("product-page", $$"""{"product": "{{catalog.Product}}", "price": "{{catalog.Product}}", "headline": "Drill"}""");

        Assert.Empty((await RelationsAsync(scenario, catalog.Product)).Value.Incoming);

        await scenario.RunAsync(entries => entries.DeleteAsync(catalog.Height, 1));
        Assert.DoesNotContain((await RelationsAsync(scenario, catalog.Product)).Value.Outgoing, relation => relation.ToId == catalog.Height);

        await scenario.RunAsync(entries => entries.DeleteAsync(catalog.Product, 1));
        Assert.Empty((await RelationsAsync(scenario, catalog.Category)).Value.Incoming);
        Assert.Equal(ContentErrorCodes.EntryNotFound, Assert.Single((await RelationsAsync(scenario, catalog.Product)).Errors).Code);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Unknown_entries_and_entries_that_are_not_entities_have_no_relations(string provider)
    {
        await using var scenario = await CreateAsync(provider);
        var post = (await scenario.CreateAsync("blog-post", """{"title": "News"}""")).Value.Id;

        Assert.Equal(ContentErrorCodes.EntryNotFound, Assert.Single((await RelationsAsync(scenario, Guid.CreateVersion7())).Errors).Code);
        Assert.Equal(ContentErrorCodes.EntryNotEntity, Assert.Single((await RelationsAsync(scenario, post)).Errors).Code);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_agent_reads_relations_only_within_its_scope(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);
        scenario.Initiator = ChangeSetScenario.Amir;
        await using (var scope = scenario.Services.CreateAsyncScope())
        {
            var types = scope.ServiceProvider.GetRequiredService<IContentTypeDirectory>();
            foreach (var (slug, isEntity, fields) in SchemaScenario.ReferenceModel)
            {
                Assert.True((await types.CreateAsync(new NewContentType(slug, slug, new ContentTypeDefinition(fields, isEntity)))).IsSuccess);
            }
        }

        var category = await EntryAsync(scenario, "category", $$"""{"name": {{Fact("\"Tools\"")}}}""");
        var product = await EntryAsync(scenario, "product", $$"""{"name": {{Fact("\"Drill\"")}}, "price": {{Fact("99")}}, "category": {{Fact($"\"{category}\"")}}}""");
        await scenario.UpdateAgentAsync(row => row.Types = ["product"]);
        scenario.Initiator = ChangeSetScenario.Agent;

        var outside = await RelationsAsync(scenario.Services, category);
        var inside = (await RelationsAsync(scenario.Services, product)).Value;

        Assert.Equal(AgentPermissionErrorCodes.ScopeDenied, Assert.Single(outside.Errors).Code);
        Assert.Empty(inside.Outgoing);
    }

    private static async Task<EntryScenario> CreateAsync(string provider)
    {
        var scenario = await EntryScenario.CreateAsync(provider);
        await scenario.Schema.CreateReferenceModelAsync();
        return scenario;
    }

    // A category, two specs and a product in the category with both specs, each at version 1.
    private static async Task<(Guid Category, Guid Width, Guid Height, Guid Product)> CatalogAsync(EntryScenario scenario)
    {
        var category = await IdAsync(scenario.CreateAsync("category", $$"""{"name": {{Fact("\"Tools\"")}}}"""));
        var width = await IdAsync(scenario.CreateAsync("spec", $$"""{"name": {{Fact("\"Width\"")}}}"""));
        var height = await IdAsync(scenario.CreateAsync("spec", $$"""{"name": {{Fact("\"Height\"")}}}"""));
        var product = await IdAsync(scenario.CreateAsync("product", $$"""
            {
              "name": {{Fact("\"Drill\"")}},
              "price": {{Fact("99")}},
              "category": {{Fact($"\"{category}\"")}},
              "specs": {{Fact($"[\"{width}\", \"{height}\"]")}}
            }
            """));

        return (category, width, height, product);
    }

    private static async Task<Guid> IdAsync(Task<Result<Entry>> created)
    {
        var result = await created;
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        return result.Value.Id;
    }

    private static async Task<Guid> EntryAsync(ChangeSetScenario scenario, string contentType, string fields)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        var created = await scope.ServiceProvider.GetRequiredService<IEntryDirectory>().CreateAsync(new NewEntry(contentType, Json(fields)));
        Assert.True(created.IsSuccess, string.Join("; ", created.Errors.Select(error => error.Message)));
        return created.Value.Id;
    }

    private static Task<int> PublishAsync(EntryScenario scenario, Guid entry, int version)
    {
        return scenario.Schema.QueryAsync(context => context.Entries
            .Where(row => row.Id == entry)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PublishedVersion, version)));
    }

    private static Task<Result<EntityRelations>> RelationsAsync(EntryScenario scenario, Guid entity)
    {
        return RelationsAsync(scenario.Services, entity);
    }

    private static async Task<Result<EntityRelations>> RelationsAsync(IServiceProvider services, Guid entity)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IEntityRelations>().GetAsync(entity);
    }

    private static EntityRelation Relation(Guid from, string fromType, string field, Guid to, string toType, bool latest = true, bool published = false)
    {
        return new EntityRelation(from, fromType, field, to, toType, null, latest, published);
    }
}
