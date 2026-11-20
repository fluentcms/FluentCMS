using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Content;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Content;

// How the diff's paths read (A-28): nested blocks by type and position, a component's field, an item of a list, rich text
// inside a block by its paragraphs, and a moved block whose own field changed.
public sealed class VersionDiffRowsTests
{
    private static readonly SchemaDescription Schema = new()
    {
        Types = [],
        Components = [Type("seo", Field("metaTitle"))],
        Blocks =
        [
            Type("section", Field("title"), Field("cards", "blocks", new JsonObject { ["blocks"] = new JsonArray("card") })),
            Type("card", Field("title"), Field("body", "richText")),
        ],
    };

    private static readonly SchemaTypeDescription Page = Type(
        "page",
        Field("sections", "blocks", new JsonObject { ["blocks"] = new JsonArray("section") }),
        Field("seo", "component", new JsonObject { ["component"] = "seo" }),
        Field("tags", multiple: true),
        Field("internal") with { Hidden = true });

    [Fact]
    public void NestedPathsAreNamedByBlockTypePositionAndField()
    {
        var version = new JsonObject
        {
            ["sections"] = new JsonArray(Section("s1", Card("c1", "Ada", Doc("One", "Two")), Card("c2", "Grace", Doc("Three")))),
            ["seo"] = new JsonObject { ["metaTitle"] = "Meta" },
            ["tags"] = new JsonArray("a", "b"),
        };
        var later = version.DeepClone().AsObject();
        later["sections"]![0]!["cards"]![1]!["title"] = "Grace Hopper";
        later["sections"]![0]!["cards"]![0]!["body"] = Doc("One", "New", "Two");

        var fields = VersionDiffRows.Build(Schema, Page,
        [
            Change("sections[0].cards[1].title", "changed", "Grace", "Grace Hopper"),
            Change("sections[0].cards[0].body.content[1].content[0].text", "changed", "Two", "New"),
            Change("sections[0].cards[0].body.content[2]", "added", null, new JsonObject()),
            Change("seo.metaTitle", "changed", "Meta", "Meta 2"),
            Change("tags[1]", "removed", "b", null),
            Change("internal", "changed", "x", "y"),
        ], version, later);

        fields.Select(field => field.Field.Name).ShouldBe(["sections", "seo", "tags"]);
        var sections = fields[0].Rows;
        Steps(sections[0]).ShouldBe(["section 1", "card 2", "title"]);
        (sections[0].Kind, sections[0].After!.GetValue<string>()).ShouldBe((DiffKind.Changed, "Grace Hopper"));
        sections.Skip(1).Select(row => (string.Join(" ", Steps(row)), row.Kind)).ShouldBe([("section 1 card 1 body paragraph 2", DiffKind.Added)]);
        Steps(fields[1].Rows.ShouldHaveSingleItem()).ShouldBe(["metaTitle"]);
        Steps(fields[2].Rows.ShouldHaveSingleItem()).ShouldBe(["item 2"]);
    }

    [Fact]
    public void AMovedBlockIsOneMoveWithItsOwnChangesUnderIt()
    {
        var before = new JsonObject { ["sections"] = new JsonArray(Section("s1"), Section("s2", title: "Old")) };
        var after = new JsonObject { ["sections"] = new JsonArray(Section("s2", title: "New"), Section("s1")) };

        var rows = VersionDiffRows.Build(Schema, Page,
        [
            Change("sections[0]", "added", null, after["sections"]![0]!.DeepClone()),
            Change("sections[1]", "removed", before["sections"]![1]!.DeepClone(), null),
        ], before, after).ShouldHaveSingleItem().Rows;

        rows.Select(row => (string.Join(" ", Steps(row)), row.Kind)).ShouldBe([("section 1", DiffKind.Moved), ("section 1 title", DiffKind.Changed)]);
        (rows[0].FromPosition, rows[0].ToPosition).ShouldBe((2, 1));
    }

    [Fact]
    public void AFormattingChangeOfRichTextIsOneRow()
    {
        var before = new JsonObject { ["sections"] = new JsonArray(Section("s1", Card("c1", "Ada", Doc("Same")))) };
        var after = before.DeepClone().AsObject();
        after["sections"]![0]!["cards"]![0]!["body"]!["content"]![0]!["attrs"] = new JsonObject { ["textAlign"] = "center" };

        var row = VersionDiffRows.Build(Schema, Page, [Change("sections[0].cards[0].body.content[0].attrs", "added", null, new JsonObject())], before, after)
            .ShouldHaveSingleItem().Rows.ShouldHaveSingleItem();

        (row.Formatting, string.Join(" ", Steps(row))).ShouldBe((true, "section 1 card 1 body"));
    }

    private static string[] Steps(DiffRow row) => [.. row.Where.Select(step => step.Kind switch
    {
        DiffStepKind.Block => $"{step.Block!.Name} {step.Position}",
        DiffStepKind.Item => $"item {step.Position}",
        DiffStepKind.Paragraph => $"paragraph {step.Position}",
        _ => step.Field?.Name ?? $"?{step.Name}",
    })];

    private static VersionChangeInfo Change(string path, string kind, JsonNode? before, JsonNode? after) =>
        new() { Path = path, Kind = kind, Before = before, After = after };

    private static JsonObject Section(string id, params JsonObject[] cards) => Section(id, "Section", cards);

    private static JsonObject Section(string id, string title, params JsonObject[] cards) =>
        new() { ["type"] = "section", ["id"] = id, ["title"] = title, ["cards"] = new JsonArray([.. cards]) };

    private static JsonObject Card(string id, string title, JsonObject body) => new() { ["type"] = "card", ["id"] = id, ["title"] = title, ["body"] = body };

    private static JsonObject Doc(params string[] paragraphs) => new()
    {
        ["type"] = "doc",
        ["content"] = new JsonArray([.. paragraphs.Select(text => (JsonNode)new JsonObject
        {
            ["type"] = "paragraph",
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        })]),
    };

    private static SchemaTypeDescription Type(string name, params SchemaFieldDescription[] fields) => new() { Name = name, Kind = "block", Fields = fields };

    private static SchemaFieldDescription Field(string name, string type = "text", JsonObject? settings = null, bool multiple = false) =>
        new() { Name = name, Type = type, Validation = new(), Settings = settings ?? [], Multiple = multiple };
}
