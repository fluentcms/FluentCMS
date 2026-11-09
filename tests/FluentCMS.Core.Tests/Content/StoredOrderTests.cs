using System.Text.Json.Nodes;
using FluentCMS.Core.Content;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Content;

// What jsonb gives back for what a write stored: the keys ordered shortest first, and a space after each colon and comma.
public class StoredOrderTests
{
    private const string Author = "0192f2a4-1111-7000-8000-000000000001";

    private static readonly SchemaModel Schema = SampleSchema.NewBuilder()
        .AddComponent("seo", component => component.Field("metaTitle", "text").Field("summary", "text"))
        .AddBlock("hero", block => block
            .Field("heading", "text")
            .Field("who", "relation", field => field.Relation("author"))
            .Field("note", "richText")
            .Field("details", "component", field => field.Setting(FieldSettings.Component, "seo")))
        .AddCollection("author", type => type.Field("name", "text"))
        .AddCollection("article", type => type
            .Field("sections", "blocks", field => field.Setting(FieldSettings.Blocks, new[] { "hero" }))
            .Field("faq", "component", field => field.Multiple().Setting(FieldSettings.Component, "seo"))
            .Field("body", "richText")
            .Field("metadata", "json"))
        .Build();

    private static SchemaField Field(string name) => Schema.FindContentType("article")!.FindField(name)!;

    private static string Ordered(string field, string stored) => StoredOrder.Of(Schema, Field(field), JsonNode.Parse(stored))!.ToJsonString();

    [Fact]
    public void ABlockHasItsTypeAndIdFirstThenItsFieldsInTheOrderOfTheSchema()
    {
        Ordered("sections", $$"""
            [{"id": "a1", "who": {"id": "{{Author}}", "type": "author"}, "note": {"type": "doc", "content": [{"type": "paragraph", "content": [{"text": "Hi", "type": "text"}]}]}, "type": "hero", "extra": 1, "details": {"summary": "S", "metaTitle": "T"}, "heading": "H"}]
            """).ShouldBe($$"""
            [{"type":"hero","id":"a1","heading":"H","who":{"id":"{{Author}}","type":"author"},"note":{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Hi"}]}]},"details":{"metaTitle":"T","summary":"S"},"extra":1}]
            """);
    }

    [Fact]
    public void ARepeatedComponentHasItsFieldsInTheOrderOfTheSchema()
    {
        Ordered("faq", """[{"summary": "S", "metaTitle": "T"}]""").ShouldBe("""[{"metaTitle":"T","summary":"S"}]""");
    }

    [Fact]
    public void RichTextIsWrittenAsItsSerializerWritesItAndTheDataOfAnInlineBlockInTheOrderOfItsBlock()
    {
        var stored = """
            {"type": "doc", "content": [{"type": "paragraph", "content": [{"text": "Before", "type": "text"}, {"type": "inlineBlock", "attrs": {"id": "b1", "data": {"details": {"summary": "S", "metaTitle": "T"}, "heading": "H"}, "blockType": "hero"}}]}]}
            """;

        StoredOrder.Of(Schema, new RichText(stored)).Json.ShouldBe("""
            {"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Before"},{"type":"inlineBlock","attrs":{"blockType":"hero","data":{"heading":"H","details":{"metaTitle":"T","summary":"S"}},"id":"b1"}}]}]}
            """);
    }

    [Fact]
    public void AJsonFieldHasNoFieldsToGoByAndKeepsItsOrder()
    {
        Ordered("metadata", """{"zeta": 1, "alpha": {"b": 2, "a": 1}}""").ShouldBe("""{"zeta":1,"alpha":{"b":2,"a":1}}""");
    }

    [Fact]
    public void RichTextThatIsNotADocumentIsReturnedAsRead()
    {
        var unreadable = new RichText("""{"type": "doc", "content": [""");

        StoredOrder.Of(Schema, unreadable).ShouldBeSameAs(unreadable);
    }
}
