using System.Text.Json.Nodes;
using FluentCMS.Core.Api.OpenApi;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Api.OpenApi;

public class ContentSchemasTests
{
    private static readonly SchemaModel Schema = SampleSchema.NewBuilder().AddContentType<Memo>().AddContentType<Memo.Settings>().Build();

    private static JsonObject Properties(string name, OpenApiDocumentOptions? options = null) =>
        new ContentSchemas(Schema, options ?? new()).All().Single(item => item.Name == name).Schema["properties"]!.AsObject();

    [Fact]
    public void AFieldIsNullableUnlessItsStorageHasAnEmptyValue()
    {
        var memo = Properties("entry-memo");

        memo["title"]!["type"]!.GetValue<string>().ShouldBe("string");
        memo["note"]!["type"]!.ToJsonString().ShouldBe("""["string","null"]""");
        memo["count"]!["type"]!.ToJsonString().ShouldBe("""["integer","null"]""");
        memo["labels"]!["type"]!.GetValue<string>().ShouldBe("array");
    }

    [Fact]
    public void AHiddenFieldIsLeftOutOfTheSchemaAndTheFilter()
    {
        Properties("entry-memo").ContainsKey("secret").ShouldBeFalse();

        var filter = new ContentPaths(Schema, new ContentSchemas(Schema, new())).Parameters().Single(item => item.Name == "filter-memo").Parameter;
        filter["schema"]!["properties"]!.AsObject().ContainsKey("secret").ShouldBeFalse();
        filter["schema"]!["properties"]!.AsObject().ContainsKey("title").ShouldBeTrue();
    }

    [Fact]
    public void AReadOnlyFieldIsMarkedAndHelpIsItsDescription()
    {
        var memo = Properties("entry-memo");

        memo["code"]!["readOnly"]!.GetValue<bool>().ShouldBeTrue();
        memo["title"]!["description"]!.GetValue<string>().ShouldBe("Shown in lists.");
    }

    [Fact]
    public void ARelationIsAnIdOrThePopulatedEntry()
    {
        var related = Properties("entry-memo")["related"]!;

        related["type"]!.GetValue<string>().ShouldBe("array");
        related["items"]!["anyOf"]!.ToJsonString().ShouldBe("""[{"type":"string","format":"uuid"},{"$ref":"#/components/schemas/entry-memo"}]""");
    }

    [Fact]
    public void AnExampleGoesOnTheSchemaOfItsClass()
    {
        var options = new OpenApiDocumentOptions().AddExample<Memo>(new JsonObject { ["title"] = "Groceries" });

        var memo = new ContentSchemas(Schema, options).All().Single(item => item.Name == "entry-memo").Schema;

        memo["examples"]![0]!["title"]!.GetValue<string>().ShouldBe("Groceries");
        new ContentSchemas(Schema, options).All().Single(item => item.Name == "entry-memo-settings").Schema.ContainsKey("examples").ShouldBeFalse();
    }

    [Fact]
    public void ASingletonListAnswersItsEntryWithoutTheListParameters()
    {
        var paths = new ContentPaths(Schema, new ContentSchemas(Schema, new()));
        ContentRoute list = new("/api/delivery/v1/{type}", "GET", ContentPaths.Delivery, new("list", "Lists") { Query = ContentParameters.List, Response = ContentPayload.EntryOrList });

        var operation = paths.Paths([list]).Single(item => item.Path == "/api/delivery/v1/memo-settings").Item["get"]!;

        operation["responses"]!["200"]!["content"]!["application/json"]!["schema"]!["$ref"]!.GetValue<string>().ShouldBe("#/components/schemas/entry-memo-settings");
        operation["parameters"]!.ToJsonString().ShouldNotContain("filter-memo-settings");
        operation["parameters"]!.ToJsonString().ShouldNotContain("#/components/parameters/cursor");
        operation["operationId"]!.GetValue<string>().ShouldBe("deliveryListMemoSettings");
    }

    [Fact]
    public void ATypeWithoutAUniqueFieldHasNoReadByField()
    {
        var paths = new ContentPaths(Schema, new ContentSchemas(Schema, new()));
        ContentRoute by = new("/api/delivery/v1/{type}/by/{field}/{value}", "GET", ContentPaths.Delivery, new("getBy", "Reads") { Query = ContentParameters.Single });

        paths.Paths([by]).Select(item => item.Path).ShouldBe(["/api/delivery/v1/memo/by/{field}/{value}"]);
    }

    [FluentCMS.Schema.Collection("memo")]
    public sealed class Memo : ContentItem
    {
        [Text, Required, Unique, Display(Help = "Shown in lists.")]
        public string Title { get; set; } = "";

        [Text]
        public string? Note { get; set; }

        [Text, Hidden]
        public string? Secret { get; set; }

        [Text, ReadOnly]
        public string? Code { get; set; }

        [Integer]
        public long? Count { get; set; }

        [Select("red", "green")]
        public List<string> Labels { get; set; } = [];

        [Relation]
        public RefList<Memo> Related { get; set; } = [];

        [Singleton("memo-settings")]
        public sealed class Settings : ContentItem
        {
            [Text]
            public string? Motto { get; set; }
        }
    }
}
