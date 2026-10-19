using System.Text.Json;
using FluentCMS.Core.Delivery;
using FluentCMS.Core.Schema;

namespace FluentCMS.Tests.Delivery;

public sealed class EntryMarkdownTests
{
    private static readonly Guid Page = Guid.Parse("0192f3a1-7c2e-7a4b-9d3e-1f2a3b4c5d6e");
    private static readonly Guid Product = Guid.Parse("0192f3a1-0000-7000-8000-000000000001");
    private static readonly Guid Hero = Guid.Parse("0192f3a1-0000-7000-8000-000000000002");

    private static readonly ContentTypeDefinition PageType = new(
    [
        Field("product", "reference", """{"target": "product"}"""),
        Field("price", "reference", """{"target": "product", "field": "price"}"""),
        Field("headline", "text"),
        Field("body", "richText"),
        Field("tags", "list", """{"itemType": "text"}"""),
        Field("related", "list", """{"itemType": "reference", "itemSettings": {"target": "product"}}"""),
        Field("hero", "media"),
        Field("featured", "boolean"),
        Field("extra", "json"),
        Field("note", "text"),
    ]);

    private static readonly ContentTypeDefinition ProductType = new([Field("name", "text"), Field("price", "number")], isEntity: true);

    [Fact]
    public void A_page_renders_front_matter_its_title_and_each_field_by_its_type()
    {
        var item = Item(PageType, "product-page", $$"""
            {
              "product": "{{Product}}",
              "price": 99.5,
              "headline": "A  sturdy\ndrill",
              "body": "Built for **daily** use.\n\n- Two speeds\n",
              "tags": ["tools", "drills"],
              "related": ["{{Product}}"],
              "hero": "{{Hero}}",
              "featured": true,
              "extra": {"warranty": 2},
              "note": null
            }
            """);

        var markdown = EntryMarkdown.Render(item, $"/api/v1/entries/{Page}.md", id => $"/api/v1/entries/{id}.md");

        Assert.Equal(
            $$"""
            ---
            id: {{Page}}
            type: product-page
            version: 3
            url: /api/v1/entries/{{Page}}.md
            ---

            # A sturdy drill

            ## product

            [{{Product}}](/api/v1/entries/{{Product}}.md)

            ## price

            99.5

            ## body

            Built for **daily** use.

            - Two speeds

            ## tags

            - tools
            - drills

            ## related

            - [{{Product}}](/api/v1/entries/{{Product}}.md)

            ## hero

            Media asset `{{Hero}}`

            ## featured

            true

            ## extra

            ```json
            {
              "warranty": 2
            }
            ```

            """.Replace("\r\n", "\n", StringComparison.Ordinal),
            markdown);
    }

    [Fact]
    public void An_entity_shows_each_facts_value_with_its_source()
    {
        var item = Item(ProductType, "product", """
            {
              "name": {"value": "Drill", "source": null, "owner": null, "validFrom": null, "validUntil": null, "lastVerified": null},
              "price": {"value": 99, "source": {"kind": "url", "reference": "https://example.com/prices"}, "owner": null, "validFrom": null, "validUntil": null, "lastVerified": null}
            }
            """);

        var markdown = EntryMarkdown.Render(item, "/x.md", id => $"/{id}.md");

        Assert.Contains("# Drill\n", markdown, StringComparison.Ordinal);
        Assert.EndsWith("## price\n\n99\n\nSource: https://example.com/prices (url)\n", markdown, StringComparison.Ordinal);
        Assert.Equal("Drill", EntryMarkdown.Title(item.Fields, ProductType));
    }

    [Fact]
    public void Without_text_the_heading_is_the_type_name_and_the_id()
    {
        var item = Item(PageType, "product-page", $$"""{"product": "{{Product}}", "headline": "  "}""");

        var markdown = EntryMarkdown.Render(item, "/x.md", id => $"/{id}.md");

        Assert.Contains($"# Product page {Page}\n", markdown, StringComparison.Ordinal);
        Assert.Null(EntryMarkdown.Title(item.Fields, PageType));
        Assert.DoesNotContain("## headline", markdown, StringComparison.Ordinal);
    }

    private static PublishedItem Item(ContentTypeDefinition definition, string type, string fields)
    {
        return new PublishedItem(
            Page,
            type,
            type == "product" ? "Product" : "Product page",
            definition,
            3,
            JsonSerializer.Deserialize<JsonElement>(fields),
            []);
    }

    private static FieldDefinition Field(string name, string type, string settings = "{}")
    {
        return new FieldDefinition(name, type, settings: JsonSerializer.Deserialize<JsonElement>(settings));
    }
}
