using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Bunit;
using FluentCMS.Admin.Components.Ui;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Schema;
using Microsoft.AspNetCore.Components.Web;

namespace FluentCMS.Tests.Admin;

public sealed class DiffViewTests
{
    private static readonly Guid Item = Guid.NewGuid();

    private static readonly ContentTypeDefinition Product = Definition(
        """
        {"entity":false,"fields":[
          {"name":"title","type":"text","required":true,"settings":{"maxLength":200}},
          {"name":"price","type":"number","required":false,"settings":{"min":0}},
          {"name":"color","type":"text","required":false,"settings":{}}
        ]}
        """);

    private static readonly ContentTypeDefinition RevisedProduct = Definition(
        """
        {"entity":false,"fields":[
          {"name":"title","type":"text","required":true,"settings":{"maxLength":200}},
          {"name":"price","type":"number","required":true,"settings":{"min":0}},
          {"name":"stock","type":"number","required":false,"settings":{"min":0}}
        ]}
        """);

    [Fact]
    public void Renders_one_block_per_item_with_its_operation_kind_and_versions_in_operation_order()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<DiffView>(view => view.Add(v => v.Diff, Diff(
            ItemDiff.ForSchema(Operation(OperationKind.Update, 3, 4, Item), Product, RevisedProduct),
            ItemDiff.ForSchema(Operation(OperationKind.Create, null, 1), null, Product),
            ItemDiff.ForSchema(Operation(OperationKind.Delete, 2, null), Product, null))));

        var items = cut.FindAll("li > [data-slot=diff-view-item]");

        Assert.Equal(["update", "create", "delete"], items.Select(item => item.GetAttribute("data-operation")));
        Assert.All(items, item => Assert.Equal("schema", item.GetAttribute("data-item-kind")));
        Assert.Equal(["Update", "Create", "Delete"], items.Select(item => item.QuerySelector("summary [data-slot=badge]")!.TextContent.Trim()));
        Assert.Equal(["secondary", "default", "destructive"], items.Select(item => item.QuerySelector("summary [data-slot=badge]")!.GetAttribute("data-variant")));
        Assert.Equal(
            ["Version 3 to 4", "Version 1", "Version 2"],
            items.Select(item => item.QuerySelector("[data-slot=diff-view-item-versions]")!.TextContent));
        Assert.Contains("Schema", items[0].QuerySelector("summary")!.TextContent);
        Assert.Contains(Item.ToString(), items[0].QuerySelector("summary")!.TextContent);
    }

    [Fact]
    public void Each_change_kind_shows_its_label_and_the_json_before_and_after()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<DiffView>(view => view.Add(v => v.Diff, Diff(
            ItemDiff.ForSchema(Operation(OperationKind.Update, 1, 2), Product, RevisedProduct))));

        var rows = cut.FindAll("[data-group=fields] tbody tr");

        Assert.Equal(["changed", "added", "removed"], rows.Select(row => row.GetAttribute("data-change")));
        Assert.Equal(["price", "stock", "color"], rows.Select(row => row.QuerySelector("th[scope=row]")!.TextContent));
        Assert.Equal(["Changed", "Added", "Removed"], rows.Select(row => row.QuerySelector("[data-slot=badge]")!.TextContent.Trim()));
        Assert.Equal(["secondary", "default", "destructive"], rows.Select(row => row.QuerySelector("[data-slot=badge]")!.GetAttribute("data-variant")));
        Assert.Equal(
            """
            {
              "type": "number",
              "required": false,
              "settings": {
                "min": 0
              }
            }
            """.ReplaceLineEndings("\n"),
            Side(rows[0], "before")!.TextContent.ReplaceLineEndings("\n"));
        Assert.Contains("\"required\": true", Side(rows[0], "after")!.TextContent);
        Assert.Equal(["Name", "Change", "Before", "After"], cut.FindAll("[data-group=fields] thead th").Select(head => head.TextContent));
    }

    [Fact]
    public void An_absent_side_renders_empty_rather_than_as_null()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<DiffView>(view => view.Add(v => v.Diff, Diff(
            ItemDiff.ForSchema(Operation(OperationKind.Update, 1, 2), Product, RevisedProduct))));

        var added = cut.Find("tr[data-change=added]");
        var removed = cut.Find("tr[data-change=removed]");

        Assert.Empty(Cell(added, "before").Children);
        Assert.Equal(string.Empty, Cell(added, "before").TextContent.Trim());
        Assert.NotNull(Side(added, "after"));
        Assert.NotNull(Side(removed, "before"));
        Assert.Empty(Cell(removed, "after").Children);
        Assert.Equal(string.Empty, Cell(removed, "after").TextContent.Trim());
        Assert.DoesNotContain("null", cut.Find("[data-slot=diff-view]").TextContent);
    }

    [Fact]
    public void Properties_and_fields_render_as_separate_labelled_tables()
    {
        using var context = new AdminTestContext();
        var entity = Definition("""{"entity":true,"fields":[{"name":"price","type":"number","required":false,"settings":{"min":0}}]}""");

        var cut = context.Render<DiffView>(view => view.Add(v => v.Diff, Diff(
            ItemDiff.ForSchema(Operation(OperationKind.Update, 1, 2), Product, entity),
            ItemDiff.ForSchema(Operation(OperationKind.Update, 1, 2), Product, RevisedProduct))));

        var items = cut.FindAll("[data-slot=diff-view-item]");
        var groups = items[0].QuerySelectorAll("[data-slot=diff-view-values]");

        Assert.Equal(["properties", "fields"], groups.Select(group => group.GetAttribute("data-group")));
        Assert.All(groups, group =>
        {
            var table = group.QuerySelector("table")!;
            var label = cut.Find($"#{table.GetAttribute("aria-labelledby")}");
            Assert.True(group.Contains(label));
        });
        Assert.Equal(["Properties", "Fields"], groups.Select(group => group.QuerySelector("table")!.GetAttribute("aria-labelledby")).Select(id => cut.Find($"#{id}").TextContent));
        var property = Assert.Single(groups[0].QuerySelectorAll("tbody tr"));
        Assert.Equal(ItemDiff.EntityProperty, property.QuerySelector("th")!.TextContent);
        Assert.Equal(["false", "true"], [Side(property, "before")!.TextContent, Side(property, "after")!.TextContent]);
        Assert.Equal(["title", "color"], groups[1].QuerySelectorAll("th[scope=row]").Select(head => head.TextContent));

        Assert.Equal(["fields"], items[1].QuerySelectorAll("[data-slot=diff-view-values]").Select(group => group.GetAttribute("data-group")));
        Assert.NotEqual(
            items[0].QuerySelector("[data-group=fields] table")!.GetAttribute("aria-labelledby"),
            items[1].QuerySelector("[data-group=fields] table")!.GetAttribute("aria-labelledby"));
    }

    [Fact]
    public void An_item_without_differences_and_a_set_without_items_say_so()
    {
        using var context = new AdminTestContext();

        var unchanged = context.Render<DiffView>(view => view.Add(v => v.Diff, Diff(
            ItemDiff.ForSchema(Operation(OperationKind.Update, 1, 2), Product, Product))));
        var empty = context.Render<DiffView>(view => view.Add(v => v.Diff, Diff()));

        Assert.Contains("No differences.", unchanged.Find("[data-slot=diff-view-item]").TextContent);
        Assert.Empty(unchanged.FindAll("table"));
        Assert.Equal("The change set has no items.", empty.Find("[data-slot=diff-view-empty]").TextContent);
        Assert.Empty(empty.FindAll("ol"));
    }

    [Fact]
    public void Items_are_native_disclosures_open_and_in_the_tab_order_so_the_keyboard_folds_them_without_script()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<DiffView>(view => view.Add(v => v.Diff, Diff(
            ItemDiff.ForSchema(Operation(OperationKind.Update, 1, 2), Product, RevisedProduct),
            ItemDiff.ForSchema(Operation(OperationKind.Create, null, 1), null, Product))));

        var items = cut.FindAll("ol[role=list] > li > details");

        Assert.Equal(2, items.Count);
        Assert.All(items, item =>
        {
            Assert.True(item.HasAttribute("open"));
            var summary = item.FirstElementChild!;
            Assert.Equal("SUMMARY", summary.TagName);
            Assert.False(summary.HasAttribute("tabindex"));
            Assert.Contains("focus-visible:ring-[3px]", summary.ClassList);
            Assert.Equal("true", summary.QuerySelector("svg")!.GetAttribute("aria-hidden"));
        });
        Assert.Empty(cut.FindAll("[onclick], [onkeydown], [tabindex='-1']"));
        Assert.Empty(cut.FindAll("[data-ui]"));
    }

    [Fact]
    public void A_roving_view_puts_only_the_first_summary_in_the_tab_order_for_admin_js_to_move()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<DiffView>(view => view
            .Add(v => v.Roving, true)
            .Add(v => v.Diff, Diff(
                ItemDiff.ForSchema(Operation(OperationKind.Update, 1, 2), Product, RevisedProduct),
                ItemDiff.ForSchema(Operation(OperationKind.Create, null, 1), null, Product),
                ItemDiff.ForSchema(Operation(OperationKind.Delete, 1, null), Product, null))));

        Assert.Equal("rove", cut.Find("ol[role=list]").GetAttribute("data-ui"));
        var summaries = cut.FindAll("summary");
        Assert.All(summaries, summary => Assert.Equal("rove-item", summary.GetAttribute("data-ui")));
        Assert.Equal([null, "-1", "-1"], summaries.Select(summary => summary.GetAttribute("tabindex")));
    }

    [Fact]
    public void An_interactive_view_renders_a_large_set_in_batches_and_is_busy_until_the_last()
    {
        using var context = new AdminTestContext();
        var large = Diff(Enumerable.Range(1, 200)
            .Select(version => ItemDiff.ForSchema(Operation(OperationKind.Update, version, version + 1), Product, RevisedProduct))
            .ToArray());

        var cut = context.Render<DiffView>(view => view.Add(v => v.Diff, Diff()).SetAssignedRenderMode(RenderMode.InteractiveServer));
        var counts = new List<int>();
        var busy = new List<string?>();
        cut.OnMarkupUpdated += (_, _) =>
        {
            counts.Add(cut.FindAll("[data-slot=diff-view-item]").Count);
            busy.Add(cut.Find("ol").GetAttribute("aria-busy"));
        };

        cut.Render(view => view.Add(v => v.Diff, large));

        cut.WaitForAssertion(() => Assert.Equal(200, cut.FindAll("[data-slot=diff-view-item]").Count));
        Assert.Equal([25, 50, 75, 100, 125, 150, 175, 200], counts);
        Assert.Equal([.. Enumerable.Repeat<string?>("true", 7), null], busy);
    }

    [Fact]
    public void A_static_view_renders_every_item_at_once()
    {
        using var context = new AdminTestContext();
        var large = Diff(Enumerable.Range(1, 200)
            .Select(version => ItemDiff.ForSchema(Operation(OperationKind.Update, version, version + 1), Product, RevisedProduct))
            .ToArray());

        var cut = context.Render<DiffView>(view => view.Add(v => v.Diff, large));

        Assert.Equal(200, cut.FindAll("[data-slot=diff-view-item]").Count);
        Assert.Equal(1, cut.RenderCount);
        Assert.False(cut.Find("ol").HasAttribute("aria-busy"));
    }

    [Fact]
    public void Uses_logical_utilities_and_a_direction_neutral_chevron_so_the_view_follows_the_root_direction()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<DiffView>(view => view.Add(v => v.Diff, Diff(
            ItemDiff.ForSchema(Operation(OperationKind.Update, 1, 2), Product, RevisedProduct))));

        var classes = cut.FindAll("[class]").SelectMany(element => element.ClassList).Select(value => value.Split(':')[^1]).ToList();
        var physical = new Regex("^-?(ml|mr|pl|pr|left|right|rounded-[lrtb][lr]?|border-[lr]|text-left|text-right|float-left|float-right)(-|$)");

        Assert.DoesNotContain(classes, value => physical.IsMatch(value));
        Assert.Contains("ms-auto", cut.Find("[data-slot=diff-view-item-versions]").ClassList);
        Assert.Contains("text-start", cut.Find("summary").ClassList);
        Assert.All(cut.FindAll("th[scope=row]"), head => Assert.Contains("text-start", head.ClassList));
        Assert.Contains("group-open:rotate-180", cut.Find("summary svg").ClassList);
    }

    [Fact]
    public void Json_reads_left_to_right_and_identifiers_are_isolated_so_right_to_left_does_not_reorder_them()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<DiffView>(view => view.Add(v => v.Diff, Diff(
            ItemDiff.ForSchema(Operation(OperationKind.Update, 1, 2, Item), Product, RevisedProduct))));

        Assert.All(cut.FindAll("td[data-side] pre"), pre => Assert.Equal("ltr", pre.GetAttribute("dir")));
        Assert.Equal(Item.ToString(), cut.Find("summary bdi").TextContent);
        Assert.Equal(["price", "stock", "color"], cut.FindAll("th[scope=row] > bdi").Select(name => name.TextContent));
    }

    [Fact]
    public void Values_keep_non_latin_text_readable_and_render_markup_as_text()
    {
        using var context = new AdminTestContext();
        var labelled = Definition("""{"entity":false,"fields":[{"name":"title","type":"text","required":true,"settings":{"label":"عنوان <b>"}}]}""");

        var cut = context.Render<DiffView>(view => view.Add(v => v.Diff, Diff(
            ItemDiff.ForSchema(Operation(OperationKind.Create, null, 1), null, labelled))));

        var after = Side(cut.Find("[data-group=fields] tbody tr"), "after")!;

        Assert.Contains("\"label\": \"عنوان <b>\"", after.TextContent);
        Assert.Empty(after.QuerySelectorAll("b"));
    }

    private static ChangeSetDiff Diff(params ItemDiff[] items) => new(Guid.NewGuid(), items);

    private static ChangeOperation Operation(OperationKind kind, int? baseVersion, int? proposedVersion, Guid? itemId = null)
    {
        return ChangeOperation.Create(kind, ItemKind.Schema, itemId ?? Guid.NewGuid(), baseVersion, proposedVersion).Value;
    }

    private static ContentTypeDefinition Definition(string json) => ContentTypeDefinition.Parse(json).Value;

    private static IElement Cell(IElement row, string side) => row.QuerySelector($"td[data-side={side}]")!;

    private static IElement? Side(IElement row, string side) => Cell(row, side).QuerySelector("pre");
}
