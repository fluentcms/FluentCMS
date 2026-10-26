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

    [Fact]
    public void An_entry_update_lists_each_changed_field_in_the_definitions_order()
    {
        var operation = EntryOperation(OperationKind.Update, 1, 2);

        var diff = ItemDiff.ForEntry(
            operation,
            Json("""{"title": "Widget", "price": 10, "color": "red", "legacy": "old"}"""),
            Json("""{"color": "blue", "price": 10.0, "title": "Widget Pro"}"""),
            Product);

        Assert.Empty(diff.Properties);
        Assert.Equal(
            [("title", ValueChange.Changed), ("color", ValueChange.Changed), ("legacy", ValueChange.Removed)],
            diff.Fields.Select(field => (field.Name, field.Change)));
        AssertJson("\"Widget\"", diff.Fields[0].Before);
        AssertJson("\"Widget Pro\"", diff.Fields[0].After);
    }

    [Fact]
    public void An_entitys_field_is_its_whole_fact()
    {
        var fact = """{"value": 10, "source": null, "owner": null, "validFrom": null, "validUntil": null, "lastVerified": null}""";
        var sourced = """{"value": 10, "source": {"kind": "url", "reference": "https://example.com"}, "owner": null, "validFrom": null, "validUntil": null, "lastVerified": null}""";

        var diff = ItemDiff.ForEntry(
            ChangeOperation.Create(OperationKind.Update, ItemKind.Entity, Item, 1, 2).Value,
            Json($$"""{"price": {{fact}}}"""),
            Json($$"""{"price": {{sourced}}}"""),
            Product);

        var price = Assert.Single(diff.Fields);
        Assert.Equal(("price", ValueChange.Changed), (price.Name, price.Change));
        AssertJson(sourced, price.After);
    }

    [Fact]
    public void An_entry_create_lists_every_field_as_added_and_a_delete_as_removed()
    {
        var fields = Json("""{"color": "red", "title": "Widget"}""");

        var create = ItemDiff.ForEntry(EntryOperation(OperationKind.Create, null, 1), null, fields, Product);
        var delete = ItemDiff.ForEntry(EntryOperation(OperationKind.Delete, 1, null), fields, null, Product);

        Assert.Equal([("title", ValueChange.Added), ("color", ValueChange.Added)], create.Fields.Select(field => (field.Name, field.Change)));
        Assert.Equal([("title", ValueChange.Removed), ("color", ValueChange.Removed)], delete.Fields.Select(field => (field.Name, field.Change)));
        Assert.All(create.Fields, field => Assert.Null(field.Before));
        Assert.All(delete.Fields, field => Assert.Null(field.After));
    }

    [Fact]
    public void An_entry_diff_is_only_for_entry_operations_with_fields_for_their_versions()
    {
        var fields = Json("""{"title": "Widget"}""");

        Assert.Throws<ArgumentException>(() => ItemDiff.ForEntry(Update(), fields, fields, Product));
        Assert.Throws<ArgumentException>(() => ItemDiff.ForEntry(EntryOperation(OperationKind.Update, 1, 2), null, fields, Product));
        Assert.Throws<ArgumentException>(() => ItemDiff.ForEntry(EntryOperation(OperationKind.Create, null, 1), fields, fields, Product));
    }

    private static ChangeOperation EntryOperation(OperationKind kind, int? baseVersion, int? proposedVersion)
    {
        return ChangeOperation.Create(kind, ItemKind.Entry, Item, baseVersion, proposedVersion).Value;
    }

    private static JsonElement Json(string json)
    {
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    [Fact]
    public void A_restored_diff_holds_what_it_was_restored_from()
    {
        var diff = ItemDiff.ForSchema(Update(), Product, WithPrice("""{"name":"price","type":"number","required":true,"settings":{"min":1}}"""));

        var restored = ItemDiff.Restore(
            diff.Operation,
            [.. diff.Properties.Select(value => ValueDiff.Restore(value.Name, value.Change, value.Before, value.After))],
            [.. diff.Fields.Select(value => ValueDiff.Restore(value.Name, value.Change, value.Before, value.After))]);

        Assert.Equal(diff.Operation, restored.Operation);
        Assert.Equal(diff.Fields.Select(value => (value.Name, value.Change)), restored.Fields.Select(value => (value.Name, value.Change)));
        Assert.All(diff.Fields.Zip(restored.Fields), pair => Assert.Equal(pair.First.After?.GetRawText(), pair.Second.After?.GetRawText()));
        Assert.Throws<ArgumentException>(() => ValueDiff.Restore("", ValueChange.Added, null, null));
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
