using System.Text.Json;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Persistence;
using FluentCMS.Infrastructure.Schema;
using static FluentCMS.IntegrationTests.Schema.SchemaScenario;

namespace FluentCMS.IntegrationTests.Schema;

// Entity types (SCH-03) and references to an entity or one of its fields (SCH-04) through the content type directory.
public sealed class EntityTypeTests
{
    private static readonly FieldDefinition[] ProductFields =
    [
        Field("name", "text", required: true),
        Field("price", "number", """{"min": 0}""", required: true),
        Field("currency", "enum", """{"values": ["EUR", "USD"]}"""),
    ];

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_entity_type_is_stored_with_its_flag_and_reads_back_as_one(string provider)
    {
        await using var scenario = await SchemaScenario.CreateAsync(provider);

        await scenario.CreateEntityAsync("product", ProductFields);
        var product = (await scenario.RunAsync(directory => directory.GetAsync("product"))).Value;
        var stored = JsonSerializer.Deserialize<JsonElement>(Assert.Single(await scenario.VersionRowsAsync()).Definition);

        Assert.True(product.Definition.IsEntity);
        Assert.True(stored.GetProperty("entity").GetBoolean());
    }

    // Definitions stored before the flag existed have no 'entity' property.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_stored_definition_without_the_flag_reads_as_a_content_type(string provider)
    {
        await using var scenario = await SchemaScenario.CreateAsync(provider);
        var id = Guid.NewGuid();

        await scenario.QueryAsync(async context =>
        {
            context.ContentTypes.Add(new ContentTypeRow { Id = id, SiteId = Site.Default.Id, Name = "Page", Slug = "page", Version = 1 });
            context.ContentTypeVersions.Add(new ContentTypeVersionRow
            {
                Id = Guid.NewGuid(),
                ContentTypeId = id,
                Version = 1,
                Definition = """{"fields":[{"name":"title","type":"text","required":true,"settings":{}}]}""",
            });
            return await context.SaveChangesAsync();
        });

        var page = (await scenario.RunAsync(directory => directory.GetAsync("page"))).Value;

        Assert.False(page.Definition.IsEntity);
        Assert.Equal("title", Assert.Single(page.Definition.Fields).Name);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_entity_type_keeps_its_flag_and_a_content_type_can_take_it(string provider)
    {
        await using var scenario = await SchemaScenario.CreateAsync(provider);
        await scenario.CreateEntityAsync("product", ProductFields);
        await scenario.CreateAsync("region", Field("name", "text"));

        var dropped = await scenario.ReviseAsync("product", 1, new ContentTypeDefinition(ProductFields));
        var flagged = await scenario.ReviseAsync("region", 1, new ContentTypeDefinition([Field("name", "text")], isEntity: true));

        Assert.Equal(SchemaErrorCodes.EntityFlagRemoved, Assert.Single(dropped.Errors).Code);
        Assert.True((await scenario.RunAsync(directory => directory.GetAsync("product"))).Value.Definition.IsEntity);
        Assert.True(flagged.Value.Definition.IsEntity);
        Assert.Equal(3, (await scenario.VersionRowsAsync()).Count);
        Assert.Equal(3, (await scenario.AuditRowsAsync()).Count);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_reference_can_target_one_field_of_an_entity(string provider)
    {
        await using var scenario = await SchemaScenario.CreateAsync(provider);
        await scenario.CreateEntityAsync("product", ProductFields);

        var created = await scenario.CreateAsync(
            "product-page",
            Reference("product", "product"),
            Reference("price", "product", "price"),
            References("prices", "product", "price"));

        Assert.True(created.IsSuccess, string.Join("; ", created.Errors.Select(error => error.Message)));
        var read = (await scenario.RunAsync(directory => directory.GetAsync("product-page"))).Value;
        Assert.Equal("price", read.Definition.Fields[1].Settings.GetProperty("field").GetString());
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_field_reference_needs_an_entity_type_that_has_the_field(string provider)
    {
        await using var scenario = await SchemaScenario.CreateAsync(provider);
        await scenario.CreateEntityAsync("product", ProductFields);
        await scenario.CreateAsync("blog-post", Field("title", "text"));

        var result = await scenario.CreateAsync(
            "product-page",
            Reference("cost", "product", "cost"),
            Reference("headline", "blog-post", "title"),
            References("costs", "product", "cost"));

        Assert.Equal(
            [
                "Field 'cost': the entity type 'product' has no field 'cost'.",
                "Field 'headline': 'blog-post' is not an entity type, so its fields cannot be referenced; reference its entries instead.",
                "Field 'costs': the entity type 'product' has no field 'cost'.",
            ],
            result.Errors.Select(error => error.Message));
        Assert.Equal(
            [SchemaErrorCodes.ReferenceFieldUnknown, SchemaErrorCodes.ReferenceTargetNotEntity, SchemaErrorCodes.ReferenceFieldUnknown],
            result.Errors.Select(error => error.Code));
        Assert.Equal(2, (await scenario.ContentTypeRowsAsync()).Count);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_field_that_another_type_references_cannot_be_removed(string provider)
    {
        await using var scenario = await SchemaScenario.CreateAsync(provider);
        await scenario.CreateEntityAsync("product", ProductFields);
        await scenario.CreateAsync("product-page", Reference("price", "product", "price"));
        await scenario.CreateAsync("comparison-page", References("prices", "product", "price"));

        var withoutPrice = await scenario.ReviseAsync("product", 1, new ContentTypeDefinition([ProductFields[0], ProductFields[2]], isEntity: true));
        var withoutCurrency = await scenario.ReviseAsync("product", 1, new ContentTypeDefinition([ProductFields[0], ProductFields[1]], isEntity: true));

        Assert.Equal(
            [
                "Field 'price' is referenced by the field 'prices' of 'comparison-page'; change that type first.",
                "Field 'price' is referenced by the field 'price' of 'product-page'; change that type first.",
            ],
            withoutPrice.Errors.Select(error => error.Message));
        Assert.All(withoutPrice.Errors, error => Assert.Equal(SchemaErrorCodes.FieldReferenced, error.Code));
        Assert.Equal(2, withoutCurrency.Value.Version);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_type_references_its_own_fields_only_as_an_entity_type(string provider)
    {
        await using var scenario = await SchemaScenario.CreateAsync(provider);

        var entity = await scenario.CreateEntityAsync("region", Field("name", "text"), Reference("alias", "region", "name"));
        var plain = await scenario.CreateAsync("page", Field("title", "text"), Reference("heading", "page", "title"));

        Assert.True(entity.IsSuccess, string.Join("; ", entity.Errors.Select(error => error.Message)));
        Assert.Equal(SchemaErrorCodes.ReferenceTargetNotEntity, Assert.Single(plain.Errors).Code);
    }
}
