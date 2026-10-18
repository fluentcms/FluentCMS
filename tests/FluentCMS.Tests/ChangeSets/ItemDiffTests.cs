using System.Text.Json;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Schema;

namespace FluentCMS.Tests.ChangeSets;

public sealed class ItemDiffTests
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

    [Fact]
    public void A_revision_lists_each_added_removed_and_changed_field_with_its_definition()
    {
        var revised = Definition(
            """
            {"entity":false,"fields":[
              {"name":"title","type":"text","required":true,"settings":{"maxLength":200}},
              {"name":"price","type":"number","required":true,"settings":{"min":0}},
              {"name":"stock","type":"number","required":false,"settings":{"min":0}}
            ]}
            """);

        var diff = ItemDiff.ForSchema(Update(), Product, revised);

        Assert.Equal(Update(), diff.Operation);
        Assert.Empty(diff.Properties);
        Assert.Equal(
            [("price", ValueChange.Changed), ("stock", ValueChange.Added), ("color", ValueChange.Removed)],
            diff.Fields.Select(field => (field.Name, field.Change)));

        var price = diff.Fields[0];
        AssertJson("""{"type":"number","required":false,"settings":{"min":0}}""", price.Before);
        AssertJson("""{"type":"number","required":true,"settings":{"min":0}}""", price.After);
        Assert.Null(diff.Fields[1].Before);
        AssertJson("""{"type":"number","required":false,"settings":{"min":0}}""", diff.Fields[1].After);
        AssertJson("""{"type":"text","required":false,"settings":{}}""", diff.Fields[2].Before);
        Assert.Null(diff.Fields[2].After);
    }

    [Theory]
    [InlineData("""{"name":"price","type":"text","required":false,"settings":{"min":0}}""")]
    [InlineData("""{"name":"price","type":"number","required":false,"settings":{"min":0,"max":10}}""")]
    [InlineData("""{"name":"price","type":"number","required":false,"settings":{}}""")]
    public void A_field_changes_when_its_type_or_settings_change(string price)
    {
        var diff = ItemDiff.ForSchema(Update(), Product, WithPrice(price));

        var field = Assert.Single(diff.Fields);
        Assert.Equal(("price", ValueChange.Changed), (field.Name, field.Change));
    }

    [Theory]
    [InlineData("""{"name":"price","type":"number","required":false,"settings":{"min":0.0}}""")]
    [InlineData("""{"name":"price","type":"number","settings":{"min":0},"required":false}""")]
    public void Settings_that_differ_only_in_formatting_are_not_changes(string price)
    {
        var diff = ItemDiff.ForSchema(Update(), Product, WithPrice(price));

        Assert.Empty(diff.Properties);
        Assert.Empty(diff.Fields);
    }

    [Fact]
    public void Flagging_a_type_as_an_entity_type_changes_the_entity_property()
    {
        var entity = Definition(
            """
            {"entity":true,"fields":[
              {"name":"title","type":"text","required":true,"settings":{"maxLength":200}},
              {"name":"price","type":"number","required":false,"settings":{"min":0}},
              {"name":"color","type":"text","required":false,"settings":{}}
            ]}
            """);

        var diff = ItemDiff.ForSchema(Update(), Product, entity);

        var property = Assert.Single(diff.Properties);
        Assert.Equal((ItemDiff.EntityProperty, ValueChange.Changed), (property.Name, property.Change));
        AssertJson("false", property.Before);
        AssertJson("true", property.After);
        Assert.Empty(diff.Fields);
    }

    [Fact]
    public void A_pure_reordering_shows_as_the_field_order_and_nothing_else()
    {
        var reordered = Definition(
            """
            {"entity":false,"fields":[
              {"name":"price","type":"number","required":false,"settings":{"min":0}},
              {"name":"title","type":"text","required":true,"settings":{"maxLength":200}},
              {"name":"color","type":"text","required":false,"settings":{}}
            ]}
            """);

        var diff = ItemDiff.ForSchema(Update(), Product, reordered);

        var property = Assert.Single(diff.Properties);
        Assert.Equal((ItemDiff.FieldOrderProperty, ValueChange.Changed), (property.Name, property.Change));
        AssertJson("""["title","price","color"]""", property.Before);
        AssertJson("""["price","title","color"]""", property.After);
        Assert.Empty(diff.Fields);
    }

    [Fact]
    public void Adding_and_removing_fields_without_moving_the_kept_ones_is_no_reordering()
    {
        var revised = Definition(
            """
            {"entity":false,"fields":[
              {"name":"sku","type":"text","required":false,"settings":{}},
              {"name":"title","type":"text","required":true,"settings":{"maxLength":200}},
              {"name":"color","type":"text","required":false,"settings":{}}
            ]}
            """);

        var diff = ItemDiff.ForSchema(Update(), Product, revised);

        Assert.Empty(diff.Properties);
        Assert.Equal([("sku", ValueChange.Added), ("price", ValueChange.Removed)], diff.Fields.Select(field => (field.Name, field.Change)));
    }

    [Fact]
    public void A_create_lists_the_entity_flag_and_every_field_as_added()
    {
        var create = ChangeOperation.Create(OperationKind.Create, ItemKind.Schema, Item, null, 1).Value;

        var diff = ItemDiff.ForSchema(create, null, Product);

        var property = Assert.Single(diff.Properties);
        Assert.Equal((ItemDiff.EntityProperty, ValueChange.Added), (property.Name, property.Change));
        Assert.Null(property.Before);
        AssertJson("false", property.After);
        Assert.Equal(["title", "price", "color"], diff.Fields.Select(field => field.Name));
        Assert.All(diff.Fields, field => Assert.Equal(ValueChange.Added, field.Change));
        Assert.All(diff.Fields, field => Assert.Null(field.Before));
        AssertJson("""{"type":"text","required":true,"settings":{"maxLength":200}}""", diff.Fields[0].After);
    }

    [Fact]
    public void A_delete_lists_the_entity_flag_and_every_field_as_removed()
    {
        var delete = ChangeOperation.Create(OperationKind.Delete, ItemKind.Schema, Item, 3, null).Value;

        var diff = ItemDiff.ForSchema(delete, Product, null);

        var property = Assert.Single(diff.Properties);
        Assert.Equal((ItemDiff.EntityProperty, ValueChange.Removed), (property.Name, property.Change));
        AssertJson("false", property.Before);
        Assert.Null(property.After);
        Assert.Equal(["title", "price", "color"], diff.Fields.Select(field => field.Name));
        Assert.All(diff.Fields, field => Assert.Equal(ValueChange.Removed, field.Change));
        Assert.All(diff.Fields, field => Assert.Null(field.After));
    }

    [Fact]
    public void A_schema_diff_needs_a_schema_operation_and_a_definition_for_each_version_it_names()
    {
        var create = ChangeOperation.Create(OperationKind.Create, ItemKind.Schema, Item, null, 1).Value;
        var entry = ChangeOperation.Create(OperationKind.Update, ItemKind.Entry, Item, 1, 2).Value;

        Assert.Throws<ArgumentException>(() => ItemDiff.ForSchema(entry, Product, Product));
        Assert.Throws<ArgumentException>(() => ItemDiff.ForSchema(create, Product, Product));
        Assert.Throws<ArgumentException>(() => ItemDiff.ForSchema(Update(), Product, null));
        Assert.Throws<ArgumentException>(() => ItemDiff.ForSchema(Update(), null, Product));
    }

    private static ChangeOperation Update()
    {
        return ChangeOperation.Create(OperationKind.Update, ItemKind.Schema, Item, 1, 2).Value;
    }

    // The product definition with its price field replaced.
    private static ContentTypeDefinition WithPrice(string price)
    {
        return Definition(
            $$$"""
            {"entity":false,"fields":[
              {"name":"title","type":"text","required":true,"settings":{"maxLength":200}},
              {{{price}}},
              {"name":"color","type":"text","required":false,"settings":{}}
            ]}
            """);
    }

    private static ContentTypeDefinition Definition(string json)
    {
        return ContentTypeDefinition.Parse(json).Value;
    }

    private static void AssertJson(string expected, JsonElement? actual)
    {
        Assert.NotNull(actual);
        using var document = JsonDocument.Parse(expected);
        Assert.True(JsonElement.DeepEquals(document.RootElement, actual.Value), $"Expected {expected} but found {actual.Value.GetRawText()}.");
    }
}
