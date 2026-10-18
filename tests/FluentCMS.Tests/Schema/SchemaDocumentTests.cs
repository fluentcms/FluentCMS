using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core;
using FluentCMS.Core.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Tests.Schema;

public sealed class SchemaDocumentTests : IDisposable
{
    private static readonly Guid Site = Guid.NewGuid();

    private readonly ServiceProvider _provider = new ServiceCollection().AddFluentCmsCore().BuildServiceProvider();

    private FieldTypeRegistry FieldTypes => _provider.GetRequiredService<FieldTypeRegistry>();

    [Fact]
    public async Task A_content_type_document_describes_every_field_and_its_example_entry_is_valid()
    {
        var document = SchemaDocument.ForType(ProductPage(), FieldTypes);

        Assert.Equal(SchemaDocument.Dialect, (string?)document["$schema"]);
        Assert.Equal("urn:fluentcms:content-type:product-page:1", (string?)document["$id"]);
        Assert.Equal("Product page", (string?)document["title"]);
        Assert.Equal("The page that sells one product. An entry of the content type 'product-page'.", (string?)document["description"]);
        Assert.False((bool)document["additionalProperties"]!);
        Assert.Equal(["product", "headline"], document["required"]!.AsArray().Select(name => (string?)name));
        Assert.Equal(
            ["product", "price", "headline", "body", "tags", "publishedOn", "featured", "extra"],
            document["properties"]!.AsObject().Select(property => property.Key));
        Assert.Equal(
            "The price shown on the page. The id of a 'product' entity; the field shows that entity's 'price' instead of a copy of it.",
            (string?)document["properties"]!["price"]!["description"]);
        Assert.Equal("Formatted text written as Markdown.", (string?)document["properties"]!["body"]!["description"]);
        Assert.Null(document["properties"]!["price"]!["properties"]);

        Assert.Empty(await JsonSchemaValidation.ErrorsAsync(document, ExampleEntry(document)));
    }

    [Fact]
    public async Task An_entity_type_document_makes_every_field_a_fact()
    {
        var document = SchemaDocument.ForType(Product(), FieldTypes);
        var price = document["properties"]!["price"]!;

        Assert.Equal(
            ["value", "source", "owner", "validFrom", "validUntil", "lastVerified"],
            price["required"]!.AsArray().Select(name => (string?)name));
        Assert.Equal("number", (string?)price["properties"]!["value"]!["type"]);
        Assert.Equal(["url", "file", "person", "system"], price["properties"]!["source"]!["properties"]!["kind"]!["enum"]!.AsArray().Select(kind => (string?)kind));
        Assert.EndsWith("It is a fact: give its value with its source, owner and validity.", (string?)price["description"]);
        Assert.True((bool)document["x-fluentcms"]!["entity"]!);

        Assert.Empty(await JsonSchemaValidation.ErrorsAsync(document, ExampleEntry(document)));
    }

    // An unsourced fact is a valid entry; it is flagged, not refused (ENT-02).
    [Fact]
    public async Task A_fact_may_state_that_it_has_no_source_yet()
    {
        var document = SchemaDocument.ForType(Product(), FieldTypes);
        var entry = ExampleEntry(document);
        entry["price"]!["source"] = null;
        entry["price"]!["owner"] = null;

        Assert.Empty(await JsonSchemaValidation.ErrorsAsync(document, entry));
    }

    [Theory]
    [InlineData("product-page", "missing required field")]
    [InlineData("product-page", "unknown field")]
    [InlineData("product-page", "date that is not a date")]
    [InlineData("product-page", "id that is not a uuid")]
    [InlineData("product", "value outside the enum")]
    [InlineData("product", "fact without its source")]
    [InlineData("product", "fact with a bare value")]
    [InlineData("product", "source of an unknown kind")]
    public async Task An_entry_that_breaks_the_document_fails_validation(string type, string breakage)
    {
        var document = SchemaDocument.ForType(type == "product" ? Product() : ProductPage(), FieldTypes);
        var entry = ExampleEntry(document);

        switch (breakage)
        {
            case "missing required field":
                entry.Remove("headline");
                break;
            case "unknown field":
                entry["subtitle"] = "Not in the schema";
                break;
            case "date that is not a date":
                entry["publishedOn"] = "next Tuesday";
                break;
            case "id that is not a uuid":
                entry["product"] = "laptop-15";
                break;
            case "value outside the enum":
                entry["currency"]!["value"] = "GBP";
                break;
            case "fact without its source":
                entry["price"]!.AsObject().Remove("source");
                break;
            case "fact with a bare value":
                entry["price"] = 1299;
                break;
            case "source of an unknown kind":
                entry["price"]!["source"]!["kind"] = "rumour";
                break;
        }

        Assert.NotEmpty(await JsonSchemaValidation.ErrorsAsync(document, entry));
    }

    [Fact]
    public async Task The_site_document_holds_every_type_under_its_slug()
    {
        var document = SchemaDocument.ForSite([ProductPage(), Product()], FieldTypes);
        var definitions = document["$defs"]!.AsObject();

        Assert.Equal(["product", "product-page"], definitions.Select(definition => definition.Key));

        foreach (var (slug, definition) in definitions)
        {
            var schema = definition!.DeepClone().AsObject();
            schema.Insert(0, "$schema", SchemaDocument.Dialect);

            Assert.True(
                (await JsonSchemaValidation.ErrorsAsync(schema, ExampleEntry(schema))).Count == 0,
                $"The example entry of '{slug}' does not satisfy its schema.");
        }
    }

    [Fact]
    public void A_field_type_that_is_no_longer_installed_is_described_as_any_value()
    {
        var restored = ContentType.Restore(
            Guid.NewGuid(),
            Site,
            "Review",
            "review",
            2,
            new ContentTypeDefinition([new FieldDefinition("rating", "sample.removed")]));

        var rating = SchemaDocument.ForType(restored, FieldTypes)["properties"]!["rating"]!.AsObject();

        Assert.Equal(["description", "examples"], rating.Select(property => property.Key));
        Assert.Equal("A value of the field type 'sample.removed', which is not installed.", (string?)rating["description"]);
    }

    public void Dispose()
    {
        _provider.Dispose();
    }

    private static JsonObject ExampleEntry(JsonObject document)
    {
        return document["examples"]![0]!.DeepClone().AsObject();
    }

    private ContentType Product()
    {
        return ContentType.Create(
            Site,
            "Product",
            "product",
            new ContentTypeDefinition(
                [
                    new FieldDefinition("name", "text", required: true, settings: Settings("""{"maxLength": 120}""")),
                    new FieldDefinition("price", "number", required: true, settings: Settings("""{"min": 0, "max": 99999.99}"""), description: "The list price."),
                    new FieldDefinition("currency", "enum", settings: Settings("""{"values": ["EUR", "USD"]}""")),
                    new FieldDefinition("hero", "media"),
                ],
                isEntity: true,
                description: "A sellable item."),
            FieldTypes).Value;
    }

    private ContentType ProductPage()
    {
        return ContentType.Create(
            Site,
            "Product page",
            "product-page",
            new ContentTypeDefinition(
                [
                    new FieldDefinition("product", "reference", required: true, settings: Settings("""{"target": "product"}""")),
                    new FieldDefinition("price", "reference", settings: Settings("""{"target": "product", "field": "price"}"""), description: "The price shown on the page."),
                    new FieldDefinition("headline", "text", required: true, settings: Settings("""{"maxLength": 5}""")),
                    new FieldDefinition("body", "richText"),
                    new FieldDefinition("tags", "list", settings: Settings("""{"itemType": "enum", "itemSettings": {"values": ["new", "sale"]}}""")),
                    new FieldDefinition("publishedOn", "dateTime", settings: Settings("""{"dateOnly": true}""")),
                    new FieldDefinition("featured", "boolean"),
                    new FieldDefinition("extra", "json"),
                ],
                description: "The page that sells one product."),
            FieldTypes).Value;
    }

    private static JsonElement Settings(string json)
    {
        return JsonSerializer.Deserialize<JsonElement>(json);
    }
}
