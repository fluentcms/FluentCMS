using System.Text.Json;
using FluentCMS.Core.Content;
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
        Field("color", "enum", """{"values": ["red", "blue"]}"""),
        Field("launch", "dateTime"),
        Field("extra", "json"),
        Field("note", "text"),
        Field("sections", "list", """{"itemType": "richText"}"""),
    ]);

    private static readonly ContentTypeDefinition ProductType = new([Field("name", "text"), Field("price", "number"), Field("claim", "text")], isEntity: true);

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
              "color": "red",
              "launch": "2026-10-18T09:00:00+00:00",
              "extra": {"warranty": 2},
              "note": null,
              "sections": ["First part.\n\nSecond paragraph.", "```\ncode\n```"]
            }
            """);

        var markdown = EntryMarkdown.Render(item, $"/api/v1/entries/{Page}.md", id => $"/api/v1/entries/{id}.md");

        Assert.Equal(
            $$"""
            ---
            id: "{{Page}}"
            type: "product-page"
            version: 3
            url: "/api/v1/entries/{{Page}}.md"
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

            ## color

            red

            ## launch

            2026-10-18T09:00:00+00:00

            ## extra

            ```json
            {
              "warranty": 2
            }
            ```

            ## sections

            - First part.

              Second paragraph.
            - ```
              code
              ```

            """.Replace("\r\n", "\n", StringComparison.Ordinal),
            markdown);
    }

    // A value is content, never structure: text that looks like a field, a source, HTML or emphasis reads as written.
    [Fact]
    public void Text_cannot_pose_as_a_field_a_source_html_or_formatting()
    {
        var page = Item(PageType, "product-page", """
            {"headline": "Learn C #", "note": "Built tough.\n\n## price\n\n0\n\n<img src=x onerror=alert(1)> costs 2*3*4\n1. first\n- item\n    indented"}
            """);
        var product = Item(ProductType, "product", """
            {
              "name": {"value": "Drill", "source": null, "owner": null, "validFrom": null, "validUntil": null, "lastVerified": null},
              "claim": {"value": "Lasts 10 years\n\nSource: https://manufacturer.example/spec (url)", "source": null, "owner": null, "validFrom": null, "validUntil": null, "lastVerified": null}
            }
            """);

        var pageMarkdown = EntryMarkdown.Render(page, "/x.md", id => $"/{id}.md");
        var productMarkdown = EntryMarkdown.Render(product, "/y.md", id => $"/{id}.md");

        Assert.Contains("# Learn C \\#\n", pageMarkdown, StringComparison.Ordinal);
        Assert.Contains(
            "## note\n\nBuilt tough.\n\n\\#\\# price\n\n0\n\n\\<img src=x onerror=alert(1)\\> costs 2\\*3\\*4\n1\\. first\n\\- item\nindented\n",
            pageMarkdown,
            StringComparison.Ordinal);
        Assert.Single(pageMarkdown.Split('\n'), line => line.StartsWith("## price", StringComparison.Ordinal) || line == "## note");
        Assert.Contains(
            "## claim\n\nLasts 10 years\n\nSource: https://manufacturer.example/spec (url)\n\n- **Source:** none, so this fact is unsourced\n",
            productMarkdown,
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_entity_shows_each_facts_value_with_its_source()
    {
        var item = Item(ProductType, "product", """
            {
              "name": {"value": "Drill", "source": null, "owner": null, "validFrom": null, "validUntil": null, "lastVerified": null},
              "price": {"value": 99, "source": {"kind": "url", "reference": "https://example.com/prices_[2026]"}, "owner": null, "validFrom": null, "validUntil": null, "lastVerified": null}
            }
            """);

        var markdown = EntryMarkdown.Render(item, "/x.md", id => $"/{id}.md");

        Assert.Contains("# Drill\n", markdown, StringComparison.Ordinal);
        Assert.EndsWith("## price\n\n99\n\n- **Source:** https://example.com/prices\\_\\[2026\\] \\(url\\)\n", markdown, StringComparison.Ordinal);
        Assert.Equal("Drill", EntryMarkdown.Title(item.Fields, ProductType));
    }

    [Fact]
    public void Without_text_the_heading_is_the_type_name_and_the_id_and_unresolved_references_are_listed()
    {
        var item = Item(PageType, "product-page", $$"""{"product": "{{Product}}", "price": null, "headline": "  "}""") with
        {
            ContentTypeName = "Product\n- [Click](https://evil.example)",
            Unresolved = [new UnresolvedReference("/price", Product, "price", UnresolvedReason.NotPublished)],
        };

        var markdown = EntryMarkdown.Render(item, "/x.md", id => $"/{id}.md");

        Assert.Contains($"# Product - \\[Click\\](https://evil.example) {Page}\n", markdown, StringComparison.Ordinal);
        Assert.Null(EntryMarkdown.Title(item.Fields, PageType));
        Assert.DoesNotContain("## headline", markdown, StringComparison.Ordinal);
        Assert.EndsWith($"## Unresolved references\n\n- `/price`: price of entry {Product} is not published\n", markdown, StringComparison.Ordinal);
    }

    // Index link text: a backslash, brackets and an autolink stay text inside the link.
    [Theory]
    [InlineData(@"C:\", @"C:\\")]
    [InlineData(@"a\[b]", @"a\\\[b\]")]
    [InlineData("<https://evil.example>", @"\<https://evil.example\>")]
    [InlineData("two\nlines", "two lines")]
    public void Inline_text_keeps_backslashes_brackets_and_autolinks_as_text(string text, string expected)
    {
        Assert.Equal(expected, EntryMarkdown.InlineText(text));
    }

    [Fact]
    public void A_backtick_in_json_cannot_close_its_fence()
    {
        var item = Item(PageType, "product-page", """{"extra": {"snippet": "```` four"}}""");

        var markdown = EntryMarkdown.Render(item, "/x.md", id => $"/{id}.md");

        Assert.Contains("```json\n{\n  \"snippet\": \"\\u0060\\u0060\\u0060\\u0060 four\"\n}\n```\n", markdown, StringComparison.Ordinal);
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
