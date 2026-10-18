using System.Text.Json;
using FluentCMS.Core;
using FluentCMS.Core.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Tests.Schema;

public sealed class ContentTypeTests : IDisposable
{
    private static readonly Guid Site = Guid.NewGuid();

    private readonly ServiceProvider _provider = new ServiceCollection().AddFluentCmsCore().BuildServiceProvider();

    private FieldTypeRegistry FieldTypes => _provider.GetRequiredService<FieldTypeRegistry>();

    [Fact]
    public void A_new_type_is_scoped_to_its_site_and_starts_at_version_one()
    {
        var definition = ProductPage();

        var contentType = ContentType.Create(Site, "  Product page ", "product-page", definition, FieldTypes).Value;

        Assert.NotEqual(Guid.Empty, contentType.Id);
        Assert.Equal(Site, contentType.SiteId);
        Assert.Equal("Product page", contentType.Name);
        Assert.Equal("product-page", contentType.Slug);
        Assert.Equal(1, contentType.Version);
        Assert.Same(definition, contentType.Definition);
    }

    [Fact]
    public void Every_problem_with_a_new_type_is_reported_at_once()
    {
        var invalid = new ContentTypeDefinition([new FieldDefinition("title", "money")]);

        var result = ContentType.Create(Guid.Empty, "   ", "Product Page", invalid, FieldTypes);

        Assert.Equal(
            [SchemaErrorCodes.SiteMissing, SchemaErrorCodes.NameInvalid, SchemaErrorCodes.SlugInvalid, SchemaErrorCodes.FieldTypeUnknown],
            result.Errors.Select(error => error.Code));
    }

    [Fact]
    public void A_name_longer_than_the_limit_is_rejected()
    {
        var result = ContentType.Create(Site, new string('a', ContentType.MaxNameLength + 1), "product", ProductPage(), FieldTypes);

        Assert.Equal(SchemaErrorCodes.NameInvalid, Assert.Single(result.Errors).Code);
    }

    [Theory]
    [InlineData("product")]
    [InlineData("product-page")]
    [InlineData("404-page")]
    public void A_slug_of_lowercase_words_joined_by_hyphens_is_accepted(string slug)
    {
        Assert.True(ContentType.Create(Site, "Type", slug, ProductPage(), FieldTypes).IsSuccess);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Product")]
    [InlineData("product page")]
    [InlineData("-product")]
    [InlineData("product-")]
    [InlineData("product--page")]
    [InlineData("product_page")]
    [InlineData("product\n")]
    public void Any_other_slug_is_rejected(string slug)
    {
        var result = ContentType.Create(Site, "Type", slug, ProductPage(), FieldTypes);

        Assert.Equal(SchemaErrorCodes.SlugInvalid, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void A_slug_longer_than_the_limit_is_rejected()
    {
        var slug = new string('a', SchemaNames.MaxLength + 1);

        Assert.Equal(SchemaErrorCodes.SlugInvalid, Assert.Single(ContentType.Create(Site, "Type", slug, ProductPage(), FieldTypes).Errors).Code);
    }

    [Fact]
    public void Revising_makes_the_next_version_and_keeps_id_site_name_and_slug()
    {
        var original = ContentType.Create(Site, "Product page", "product-page", ProductPage(), FieldTypes).Value;
        var revised = new ContentTypeDefinition([.. original.Definition.Fields, new FieldDefinition("summary", "text")]);

        var next = original.Revise(revised, FieldTypes).Value;

        Assert.Equal(2, next.Version);
        Assert.Equal(original.Id, next.Id);
        Assert.Equal(original.SiteId, next.SiteId);
        Assert.Equal(original.Name, next.Name);
        Assert.Equal(original.Slug, next.Slug);
        Assert.Same(revised, next.Definition);
        Assert.Equal(1, original.Version);
        Assert.Equal(3, next.Revise(ProductPage(), FieldTypes).Value.Version);
    }

    [Fact]
    public void An_invalid_revision_fails_and_leaves_the_type_as_it_was()
    {
        var definition = ProductPage();
        var original = ContentType.Create(Site, "Product page", "product-page", definition, FieldTypes).Value;

        var result = original.Revise(new ContentTypeDefinition([new FieldDefinition("title", "text"), new FieldDefinition("Title", "text")]), FieldTypes);

        Assert.False(result.IsSuccess);
        Assert.Equal(SchemaErrorCodes.FieldNameInvalid, Assert.Single(result.Errors).Code);
        Assert.Equal(1, original.Version);
        Assert.Same(definition, original.Definition);
    }

    public void Dispose()
    {
        _provider.Dispose();
    }

    private static ContentTypeDefinition ProductPage()
    {
        return new ContentTypeDefinition(
        [
            new FieldDefinition("title", "text", required: true, settings: Settings("""{"maxLength": 120}""")),
            new FieldDefinition("product", "reference", required: true, settings: Settings("""{"target": "product"}""")),
            new FieldDefinition("body", "richText"),
        ]);
    }

    private static JsonElement Settings(string json)
    {
        return JsonSerializer.Deserialize<JsonElement>(json);
    }
}
