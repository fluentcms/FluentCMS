using System.Text.Json.Nodes;
using FluentCMS.Core.Tests.Schema.Samples;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Schema.FieldTypes;

public class StructuredFieldTypeTests
{
    private enum Mood
    {
        Happy,
        VerySad,
    }

    private static readonly string[] TwoOptions = ["news", "opinion"];

    private static readonly string[] NewsAndVerySad = ["news", "verySad"];

    private static readonly string[] NewsAndOutside = ["news", "outside"];

    private const string EmptyDocument = """{"type":"doc","content":[{"type":"paragraph"}]}""";

    [Fact]
    public void SelectAcceptsAStringWithoutCheckingTheOptions()
    {
        TestFields.Coerce("select", "news").ShouldBe("news");
        TestFields.Coerce("select", "not-an-option").ShouldBe("not-an-option");
        TestFields.Coerce("select", TestFields.Json("\"opinion\"")).ShouldBe("opinion");
        TestFields.Coerce("select", null).ShouldBeNull();
    }

    [Fact]
    public void SelectNamesAnEnumMemberInCamelCase()
    {
        TestFields.Coerce("select", Mood.Happy).ShouldBe("happy");
        TestFields.Coerce("select", Mood.VerySad).ShouldBe("verySad");
    }

    [Fact]
    public void SelectRejectsOtherValues()
    {
        TestFields.Reject("select", 5).ShouldBe("expected a string but got a number");
        TestFields.Reject("select", TwoOptions).ShouldBe("expected a string but got a value of type String[]");
        TestFields.Reject("select", TestFields.Json("[\"news\"]")).ShouldBe("expected a string but got an array");
    }

    [Fact]
    public void MultiSelectAcceptsAListOfStrings()
    {
        void Configure(IFieldBuilder field) => field.Multiple();

        TestFields.Coerce("select", TwoOptions, Configure).ShouldBe(TwoOptions);
        TestFields.Coerce("select", new List<object> { "news", Mood.VerySad }, Configure).ShouldBe(NewsAndVerySad);
        TestFields.Coerce("select", TestFields.Json("[\"news\",\"outside\"]"), Configure).ShouldBe(NewsAndOutside);
        TestFields.Coerce("select", TestFields.Json("[]"), Configure).ShouldBe(Array.Empty<string>());
        TestFields.Coerce("select", null, Configure).ShouldBeNull();
    }

    [Fact]
    public void MultiSelectRejectsOtherValues()
    {
        void Configure(IFieldBuilder field) => field.Multiple();

        TestFields.Reject("select", "news", Configure).ShouldBe("expected a list of strings but got a string");
        TestFields.Reject("select", TestFields.Json("{}"), Configure).ShouldBe("expected a list of strings but got an object");
        TestFields.Reject("select", 5, Configure).ShouldBe("expected a list of strings but got a number");
        TestFields.Reject("select", TestFields.Json("[\"news\",1]"), Configure).ShouldBe("item 1 is not a string: it is a number");
        TestFields.Reject("select", new object?[] { "news", null }, Configure).ShouldBe("item 1 is not a string: it is null");
    }

    [Fact]
    public void JsonAcceptsAnyJsonValue()
    {
        var element = TestFields.Coerce("json", TestFields.Json("""{"a":[1,2],"b":{"c":true}}""")).ShouldBeAssignableTo<JsonNode>()!;
        element.ToJsonString().ShouldBe("""{"a":[1,2],"b":{"c":true}}""");

        TestFields.Coerce("json", """{"a":1}""").ShouldBeAssignableTo<JsonObject>()!["a"]!.GetValue<int>().ShouldBe(1);
        TestFields.Coerce("json", "[1,2,3]").ShouldBeAssignableTo<JsonArray>()!.Count.ShouldBe(3);
        TestFields.Coerce("json", TestFields.Json("\"text\"")).ShouldBeAssignableTo<JsonNode>()!.GetValue<string>().ShouldBe("text");
        TestFields.Coerce("json", TestFields.Json("12")).ShouldBeAssignableTo<JsonNode>()!.GetValue<int>().ShouldBe(12);
        TestFields.Coerce("json", true).ShouldBeAssignableTo<JsonNode>()!.GetValue<bool>().ShouldBeTrue();
        TestFields.Coerce("json", 12.5m).ShouldBeAssignableTo<JsonNode>()!.GetValue<decimal>().ShouldBe(12.5m);
        TestFields.Coerce("json", null).ShouldBeNull();
        TestFields.Coerce("json", TestFields.Json("null")).ShouldBeNull();
    }

    [Fact]
    public void JsonCopiesANodeSoTheCallerKeepsItsOwn()
    {
        var node = JsonNode.Parse("""{"a":1}""")!;

        var result = TestFields.Coerce("json", node).ShouldBeAssignableTo<JsonNode>()!;

        result.ShouldNotBeSameAs(node);
        result.ToJsonString().ShouldBe(node.ToJsonString());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"a\":")]
    [InlineData("")]
    public void JsonRejectsTextThatIsNotJson(string text)
    {
        TestFields.Reject("json", text).ShouldStartWith("is not valid JSON");
    }

    [Fact]
    public void JsonRejectsOtherObjects()
    {
        TestFields.Reject("json", new object()).ShouldBe("expected JSON but got a value of type Object");
    }

    [Fact]
    public void RichTextAcceptsADocumentInAnyForm()
    {
        TestFields.Coerce("richText", EmptyDocument).ShouldBeOfType<RichText>().Json.ShouldBe(EmptyDocument);
        TestFields.Coerce("richText", RichText.Empty).ShouldBeSameAs(RichText.Empty);
        TestFields.Coerce("richText", TestFields.Json(EmptyDocument)).ShouldBeOfType<RichText>().Json.ShouldBe(EmptyDocument);
        TestFields.Coerce("richText", """{"type":"doc"}""").ShouldBeOfType<RichText>();
        TestFields.Coerce("richText", null).ShouldBeNull();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"type":"paragraph"}""")]
    [InlineData("""{"type":"doc","content":{}}""")]
    [InlineData("""[{"type":"doc"}]""")]
    public void RichTextRejectsOtherText(string text)
    {
        TestFields.Reject("richText", text).ShouldStartWith("is not ");
    }

    [Fact]
    public void RichTextRejectsOtherValues()
    {
        TestFields.Reject("richText", 5).ShouldBe("expected a rich text document but got a number");
        TestFields.Reject("richText", TestFields.Json("[]")).ShouldBe("expected a rich text document but got an array");
        TestFields.Reject("richText", new RichText("{}")).ShouldStartWith("is not a rich text document");
    }

    [Fact]
    public void RelationAcceptsAnEntryId()
    {
        var id = Guid.Parse("0192f2a4-7b1c-7d2e-9f10-2a3b4c5d6e7f");

        TestFields.Coerce("relation", id).ShouldBe(id);
        TestFields.Coerce("relation", id.ToString()).ShouldBe(id);
        TestFields.Coerce("relation", TestFields.Json($"\"{id}\"")).ShouldBe(id);
        TestFields.Coerce("relation", new Ref<Author>(id)).ShouldBe(id);
        TestFields.Coerce("relation", null).ShouldBeNull();
    }

    [Fact]
    public void AnEmptyReferenceMeansNoRelation()
    {
        TestFields.Coerce("relation", default(Ref<Author>)).ShouldBeNull();
        TestFields.Coerce("relation", Guid.Empty).ShouldBeNull();
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    [InlineData("0192f2a47b1c7d2e9f102a3b4c5d6e7f")]
    public void RelationRejectsTextThatIsNotAnId(string text)
    {
        TestFields.Reject("relation", text).ShouldBe("is not an entry id: expected a GUID");
    }

    [Fact]
    public void RelationRejectsOtherValues()
    {
        TestFields.Reject("relation", 5).ShouldBe("is not an entry id: expected a GUID");
        TestFields.Reject("relation", TestFields.Json("[\"0192f2a4-7b1c-7d2e-9f10-2a3b4c5d6e7f\"]")).ShouldBe("is not an entry id: expected a GUID");
    }

    [Fact]
    public void ToManyRelationKeepsTheOrder()
    {
        void Configure(IFieldBuilder field) => field.Multiple();
        Guid[] ids = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

        TestFields.Coerce("relation", ids, Configure).ShouldBe(ids);
        TestFields.Coerce("relation", ids.Select(id => id.ToString()).ToList(), Configure).ShouldBe(ids);
        TestFields.Coerce("relation", new RefList<Tag>([.. ids.Select(id => new Ref<Tag>(id))]), Configure).ShouldBe(ids);
        TestFields.Coerce("relation", TestFields.Json($"[\"{ids[2]}\",\"{ids[0]}\"]"), Configure).ShouldBe(new Guid[] { ids[2], ids[0] });
        TestFields.Coerce("relation", TestFields.Json("[]"), Configure).ShouldBe(Array.Empty<Guid>());
        TestFields.Coerce("relation", null, Configure).ShouldBeNull();
    }

    [Fact]
    public void ToManyRelationRejectsOtherValues()
    {
        void Configure(IFieldBuilder field) => field.Multiple();

        TestFields.Reject("relation", Guid.NewGuid().ToString(), Configure).ShouldBe("expected a list of entry ids but got a string");
        TestFields.Reject("relation", 5, Configure).ShouldBe("expected a list of entry ids but got a number");
        TestFields.Reject("relation", TestFields.Json("""["0192f2a4-7b1c-7d2e-9f10-2a3b4c5d6e7f","x"]"""), Configure).ShouldBe("item 1 is not an entry id: expected a GUID");
        TestFields.Reject("relation", new[] { Guid.Empty }, Configure).ShouldBe("item 0 is not an entry id: expected a GUID");
    }

    [Fact]
    public void ASingleComponentIsAnObject()
    {
        TestFields.Coerce("component", TestFields.Json("""{"title":"Hi"}""")).ShouldBeOfType<JsonObject>()["title"]!.GetValue<string>().ShouldBe("Hi");
        TestFields.Coerce("component", """{"title":"Hi"}""").ShouldBeOfType<JsonObject>();
        TestFields.Coerce("component", TestFields.Json("{}")).ShouldBeOfType<JsonObject>();
        TestFields.Coerce("component", null).ShouldBeNull();
    }

    [Fact]
    public void ARepeatableComponentIsAListOfObjects()
    {
        void Configure(IFieldBuilder field) => field.Multiple();

        TestFields.Coerce("component", TestFields.Json("""[{"title":"A"},{"title":"B"}]"""), Configure).ShouldBeOfType<JsonArray>().Count.ShouldBe(2);
        TestFields.Coerce("component", TestFields.Json("[]"), Configure).ShouldBeOfType<JsonArray>().Count.ShouldBe(0);
        TestFields.Coerce("component", null, Configure).ShouldBeNull();
    }

    [Fact]
    public void ComponentRejectsTheWrongShape()
    {
        void Repeatable(IFieldBuilder field) => field.Multiple();

        TestFields.Reject("component", TestFields.Json("[{}]")).ShouldBe("is not an object");
        TestFields.Reject("component", TestFields.Json("5")).ShouldBe("is not an object");
        TestFields.Reject("component", "not json").ShouldStartWith("is not valid JSON");
        TestFields.Reject("component", new object()).ShouldBe("expected JSON but got a value of type Object");
        TestFields.Reject("component", TestFields.Json("{}"), Repeatable).ShouldBe("is not a list of objects");
        TestFields.Reject("component", TestFields.Json("[1]"), Repeatable).ShouldBe("is not a list of objects");
    }

    [Fact]
    public void BlocksAcceptAListOfBlocksThatNameTheirType()
    {
        var node = TestFields.Coerce("blocks", TestFields.Json("""[{"type":"hero","heading":"Hi"},{"type":"hero"}]""")).ShouldBeOfType<JsonArray>();
        node.Count.ShouldBe(2);

        TestFields.Coerce("blocks", """[{"type":"hero"}]""").ShouldBeOfType<JsonArray>();
        TestFields.Coerce("blocks", TestFields.Json("[]")).ShouldBeOfType<JsonArray>();
        TestFields.Coerce("blocks", null).ShouldBeNull();
    }

    [Fact]
    public void BlocksKeepAnAlreadyTypedBlockList()
    {
        BlockList blocks = [new object()];

        TestFields.Coerce("blocks", blocks).ShouldBeSameAs(blocks);
    }

    [Fact]
    public void BlocksDoNotCheckWhetherTheBlockIsAccepted()
    {
        TestFields.Coerce("blocks", TestFields.Json("""[{"type":"other"}]""")).ShouldBeOfType<JsonArray>();
    }

    [Theory]
    [InlineData("{}", "is not a list of blocks")]
    [InlineData("5", "is not a list of blocks")]
    [InlineData("[1]", "block 0 is not an object with a \"type\" naming its block")]
    [InlineData("[{}]", "block 0 is not an object with a \"type\" naming its block")]
    [InlineData("""[{"type":"hero"},{"type":""}]""", "block 1 is not an object with a \"type\" naming its block")]
    [InlineData("""[{"type":5}]""", "block 0 is not an object with a \"type\" naming its block")]
    public void BlocksRejectTheWrongShape(string json, string message)
    {
        TestFields.Reject("blocks", TestFields.Json(json)).ShouldBe(message);
    }
}
