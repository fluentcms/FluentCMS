using System.Text.Json.Nodes;
using Bunit;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Content;
using FluentCMS.Admin.Lists;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Content;

public sealed class ListViewTests : BunitContext
{
    private static readonly SchemaTypeDescription Author = new()
    {
        Name = "author",
        Kind = "collection",
        Fields = [Field("name", "text", required: true)],
    };

    [Fact]
    public void TheColumnsAreTheSystemFieldsAndTheFieldsACellCanShow()
    {
        var view = View(Article(localized: true), new ListCellRegistry());

        view.Label.Name.ShouldBe("title");
        view.Label.Sortable.ShouldBeTrue();
        view.Available.Select(column => column.Name).ShouldBe(["status", "locale", "updatedAt", "updatedBy", "rank", "tags", "author", "secretless"]);
        view.Available.Where(column => column.Sortable).Select(column => column.Name).ShouldBe(["updatedAt", "rank"]);
        view.DefaultColumns.ShouldBe(["status", "updatedAt"]);
    }

    [Fact]
    public void ARegisteredCellOffersItsFieldTypeAndATypeWithoutOneIsNotOffered()
    {
        var cells = new ListCellRegistry();
        cells.Add<StarsCell>("json");

        var view = View(Article(localized: false), cells);

        view.Available.Select(column => column.Name).ShouldBe(["status", "updatedAt", "updatedBy", "rank", "tags", "author", "secretless", "extra"]);
        cells.CellFor("json").ShouldBe(typeof(StarsCell));
        cells.CellFor("richText").ShouldBe(typeof(TextListCell));
    }

    [Fact]
    public void ARelationIsReadWithTheLabelOfItsTargetWhenTheTargetCanBeRead()
    {
        var view = View(Article(localized: false), new ListCellRegistry());

        var query = view.Query(view.Choose(["author", "tags", "rank"]), ListView.DefaultSort, 1, 25);

        query.Fields.ShouldBe(["title", "author.name", "tags", "rank"]);
        query.Populate.ShouldBe(["author"]);
        query.WithTotal.ShouldBe(true);
    }

    [Fact]
    public void ChoiceAndSortKeepOnlyWhatExists()
    {
        var view = View(Article(localized: false), new ListCellRegistry());

        view.Choose(["rank", "rank", "title", "body", "nothing"]).Select(column => column.Name).ShouldBe(["rank"]);
        view.Choose(["nothing"]).Select(column => column.Name).ShouldBe(["status", "updatedAt"]);
        view.ParseSort("-rank, title,-rank,status,nothing").ShouldBe([new SortField("rank", true), new SortField("title")]);
        view.ParseSort("status").ShouldBe(ListView.DefaultSort);
        ListView.ParsePageSize(7).ShouldBe(7);
        ListView.ParsePageSize(101).ShouldBe(25);
        ListView.ParsePageSize(100).ShouldBe(100);
        ListView.ParsePage(0).ShouldBe(1);
    }

    [Fact]
    public void ATypeWithoutALabelFieldIsListedByIdAndStillNamesAField()
    {
        var type = new SchemaTypeDescription { Name = "log", Kind = "collection", Fields = [Field("count", "integer")] };
        var view = ListView.For(type, new SchemaDescription { Types = [type], Components = [], Blocks = [] }, new ListCellRegistry());

        view.Label.Name.ShouldBe("id");
        view.Query([], ListView.DefaultSort, 1, 25).Fields.ShouldBe(["id"]);
    }

    [Fact]
    public void TheTextCellShowsScalarsListsAndPopulatedLabels()
    {
        Text("text", JsonValue.Create("Hello")).ShouldBe("Hello");
        Text("integer", JsonValue.Create(3)).ShouldBe("3");
        Text("relation", new JsonObject { ["id"] = "a1", ["type"] = "author", ["name"] = "Grace" }).ShouldBe("Grace");
        Text("relation", new JsonArray(new JsonObject { ["id"] = "t1", ["type"] = "tag" }, new JsonObject { ["id"] = "t2", ["title"] = "News" })).ShouldBe("t1, News");
        Text("text", null).ShouldBe("");
    }

    private string Text(string type, JsonNode? value)
    {
        var entry = new ContentEntry { Fields = new JsonObject { ["field"] = value } };
        var cell = Render<TextListCell>(p => p.Add(x => x.Field, Field("field", type)).Add(x => x.Entry, entry));
        return cell.Find(".cms-list-cell-text").TextContent;
    }

    private static ListView View(SchemaTypeDescription type, ListCellRegistry cells) =>
        ListView.For(type, new SchemaDescription { Types = [type, Author], Components = [], Blocks = [] }, cells);

    private static SchemaTypeDescription Article(bool localized) => new()
    {
        Name = "article",
        Kind = "collection",
        Localized = localized,
        Fields =
        [
            Field("title", "text", required: true),
            Field("rank", "integer"),
            Field("tags", "relation", multiple: true) with { Relation = new FieldRelationDescription { Target = "tag" } },
            Field("author", "relation") with { Relation = new FieldRelationDescription { Target = "author" } },
            Field("body", "richText"),
            Field("secret", "text") with { Hidden = true },
            Field("secretless", "slug", multiple: true),
            Field("extra", "json"),
        ],
    };

    private static SchemaFieldDescription Field(string name, string type, bool required = false, bool multiple = false) =>
        new() { Name = name, Type = type, Multiple = multiple, Validation = new FieldValidationDescription { Required = required } };

    private sealed class StarsCell : ListCellBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, "*");
    }
}
