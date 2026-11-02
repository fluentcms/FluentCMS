using System.Text.Json.Nodes;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Schema.FieldTypes;

// A raw value may be a CLR value, a JsonElement or a JsonNode; every type reads all three.
public class JsonNodeInputTests
{
    private static readonly Guid Id = Guid.Parse("0192f2a4-7b1c-7d2e-9f10-2a3b4c5d6e7f");

    private static readonly string[] NewsAndOpinion = ["news", "opinion"];

    private static readonly Guid[] OneId = [Id];

    [Theory]
    [InlineData("text")]
    [InlineData("longText")]
    public void TextReadsAStringNode(string type)
    {
        TestFields.Coerce(type, JsonValue.Create("hi")).ShouldBe("hi");
        TestFields.Reject(type, JsonNode.Parse("{}")).ShouldBe("expected a string but got an object");
        TestFields.Reject(type, JsonValue.Create(5)).ShouldBe("expected a string but got a number");
    }

    [Fact]
    public void ScalarTypesReadValueNodes()
    {
        TestFields.Coerce("integer", JsonValue.Create(5)).ShouldBe(5L);
        TestFields.Coerce("decimal", JsonValue.Create(1.5m)).ShouldBe(1.5m);
        TestFields.Coerce("boolean", JsonValue.Create(true)).ShouldBe(true);
        TestFields.Coerce("date", JsonValue.Create("2026-11-01")).ShouldBe(new DateOnly(2026, 11, 1));
        TestFields.Coerce("dateTime", JsonValue.Create("2026-11-01T09:12:00Z")).ShouldBe(new DateTimeOffset(2026, 11, 1, 9, 12, 0, TimeSpan.Zero));
        TestFields.Coerce("select", JsonValue.Create("news")).ShouldBe("news");
        TestFields.Coerce("relation", JsonValue.Create(Id.ToString())).ShouldBe(Id);
    }

    [Fact]
    public void ListsReadArrayNodes()
    {
        void Configure(IFieldBuilder field) => field.Multiple();

        TestFields.Coerce("select", JsonNode.Parse("""["news","opinion"]"""), Configure).ShouldBe(NewsAndOpinion);
        TestFields.Coerce("relation", JsonNode.Parse($"""["{Id}"]"""), Configure).ShouldBe(OneId);
    }

    [Fact]
    public void RichTextReadsAnObjectNode()
    {
        TestFields.Coerce("richText", JsonNode.Parse("""{"type":"doc","content":[]}""")).ShouldBeOfType<RichText>().Json.ShouldBe("""{"type":"doc","content":[]}""");
        TestFields.Reject("richText", JsonNode.Parse("[]")).ShouldBe("expected a rich text document but got an array");
    }

    [Fact]
    public void JsonComponentAndBlocksReadNodes()
    {
        TestFields.Coerce("json", JsonNode.Parse("""{"a":1}""")).ShouldBeOfType<JsonObject>();
        TestFields.Coerce("json", JsonValue.Create("hi")).ShouldBeAssignableTo<JsonNode>()!.GetValue<string>().ShouldBe("hi");
        TestFields.Coerce("component", JsonNode.Parse("""{"title":"Hi"}""")).ShouldBeOfType<JsonObject>();
        TestFields.Coerce("blocks", JsonNode.Parse("""[{"type":"hero"}]""")).ShouldBeOfType<JsonArray>();
    }
}
