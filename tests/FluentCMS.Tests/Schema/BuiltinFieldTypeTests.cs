using FluentCMS.Core.Schema;

namespace FluentCMS.Tests.Schema;

public sealed class TextFieldTypeTests : FieldTypeConformanceTests
{
    protected override string FieldTypeName => "text";

    protected override IEnumerable<string> AcceptedSettings => ["{}", """{"maxLength": 200}"""];

    protected override IEnumerable<string> RejectedSettings =>
        ["""{"maxLength": 0}""", """{"maxLength": -5}""", """{"maxLength": 1.5}""", """{"maxLength": "200"}"""];

    protected override IEnumerable<(string Settings, string Value)> AcceptedValues =>
    [
        ("{}", "\"\""),
        ("{}", "\"Plain text\\non two lines\""),
        ("""{"maxLength": 5}""", "\"héllo\""),
        ("""{"maxLength": 2}""", "\"😀😀\""),
    ];

    protected override IEnumerable<(string Settings, string Value)> RejectedValues =>
    [
        ("{}", "5"),
        ("{}", "true"),
        ("{}", """["text"]"""),
        ("""{"maxLength": 5}""", "\"too long\""),
    ];
}

public sealed class RichTextFieldTypeTests : FieldTypeConformanceTests
{
    protected override string FieldTypeName => "richText";

    protected override IEnumerable<string> AcceptedSettings => ["{}", """{"maxLength": 20000}"""];

    protected override IEnumerable<string> RejectedSettings => ["""{"maxLength": 0}""", """{"maxLength": true}"""];

    protected override IEnumerable<(string Settings, string Value)> AcceptedValues =>
    [
        ("{}", "\"Some **Markdown**.\""),
        ("""{"maxLength": 20000}""", "\"\""),
    ];

    protected override IEnumerable<(string Settings, string Value)> RejectedValues =>
    [
        ("{}", """{"markdown": "text"}"""),
        ("""{"maxLength": 3}""", "\"**a**\""),
    ];
}

public sealed class NumberFieldTypeTests : FieldTypeConformanceTests
{
    protected override string FieldTypeName => "number";

    protected override IEnumerable<string> AcceptedSettings =>
    [
        "{}",
        """{"integer": true, "min": 0, "max": 100}""",
        """{"min": -1.5, "max": 2.25}""",
        """{"min": 3, "max": 3}""",
    ];

    protected override IEnumerable<string> RejectedSettings =>
    [
        """{"min": 5, "max": 1}""",
        """{"integer": true, "min": 0.5}""",
        """{"integer": true, "max": 9.99}""",
        """{"integer": "yes"}""",
        """{"min": "0"}""",
    ];

    protected override IEnumerable<(string Settings, string Value)> AcceptedValues =>
    [
        ("{}", "0"),
        ("{}", "-1.5"),
        ("{}", "1e3"),
        ("""{"integer": true, "min": 0, "max": 100}""", "100"),
        ("""{"integer": true, "min": 0, "max": 100}""", "2.0"),
        ("""{"min": -1.5, "max": 2.25}""", "-1.5"),
    ];

    protected override IEnumerable<(string Settings, string Value)> RejectedValues =>
    [
        ("{}", "\"5\""),
        ("{}", "1e400"),
        ("""{"integer": true, "min": 0, "max": 100}""", "1.5"),
        ("""{"integer": true, "min": 0, "max": 100}""", "-1"),
        ("""{"integer": true, "min": 0, "max": 100}""", "101"),
        ("""{"min": -1.5, "max": 2.25}""", "2.26"),
    ];

    [Fact]
    public void A_maximum_below_the_minimum_is_reported_on_max()
    {
        var error = Assert.Single(Registry.ValidateSettings(FieldTypeName, Parse("""{"min": 5, "max": 1}""")));

        Assert.Equal(SchemaErrorCodes.SettingInvalid, error.Code);
        Assert.Equal("The setting 'max' must not be less than 'min'.", error.Message);
    }

    [Theory]
    [InlineData("-1", "The value must be at least 0.")]
    [InlineData("101", "The value must be at most 100.")]
    [InlineData("1.5", "The value must be a whole number.")]
    [InlineData("1e400", "The value 1e400 is too large or too precise to store.")]
    public void A_value_outside_the_settings_says_which_limit_it_breaks(string value, string message)
    {
        var error = Assert.Single(Registry.ValidateValue(FieldTypeName, Parse("""{"integer": true, "min": 0, "max": 100}"""), Parse(value)));

        Assert.Equal(message, error.Message);
    }
}

public sealed class BooleanFieldTypeTests : FieldTypeConformanceTests
{
    protected override string FieldTypeName => "boolean";

    protected override IEnumerable<string> AcceptedSettings => ["{}"];

    protected override IEnumerable<string> RejectedSettings => ["""{"default": true}"""];

    protected override IEnumerable<(string Settings, string Value)> AcceptedValues =>
    [
        ("{}", "true"),
        ("{}", "false"),
    ];

    protected override IEnumerable<(string Settings, string Value)> RejectedValues =>
    [
        ("{}", "\"true\""),
        ("{}", "1"),
    ];
}

public sealed class DateTimeFieldTypeTests : FieldTypeConformanceTests
{
    protected override string FieldTypeName => "dateTime";

    protected override IEnumerable<string> AcceptedSettings => ["{}", """{"dateOnly": true}""", """{"dateOnly": false}"""];

    protected override IEnumerable<string> RejectedSettings => ["""{"dateOnly": "yes"}""", """{"dateOnly": 1}"""];

    protected override IEnumerable<(string Settings, string Value)> AcceptedValues =>
    [
        ("{}", "\"2026-01-31T09:30:00Z\""),
        ("{}", "\"2026-01-31T09:30:00.125+02:00\""),
        ("""{"dateOnly": false}""", "\"2026-12-31T23:59:59-05:00\""),
        ("""{"dateOnly": true}""", "\"2026-01-31\""),
    ];

    protected override IEnumerable<(string Settings, string Value)> RejectedValues =>
    [
        ("{}", "\"2026-01-31\""),
        ("{}", "\"2026-01-31T09:30:00\""),
        ("{}", "\"2026-02-30T09:30:00Z\""),
        ("{}", "\"31/01/2026 09:30\""),
        ("{}", "20260131"),
        ("""{"dateOnly": true}""", "\"2026-01-31T09:30:00Z\""),
        ("""{"dateOnly": true}""", "\"2026-13-01\""),
    ];
}

public sealed class EnumFieldTypeTests : FieldTypeConformanceTests
{
    protected override string FieldTypeName => "enum";

    protected override IEnumerable<string> AcceptedSettings => ["""{"values": ["draft", "live"]}""", """{"values": ["only"]}"""];

    protected override IEnumerable<string> RejectedSettings =>
    [
        "{}",
        """{"values": []}""",
        """{"values": ["a", "a"]}""",
        """{"values": ["a", " "]}""",
        """{"values": "a"}""",
        """{"values": [1]}""",
    ];

    protected override IEnumerable<(string Settings, string Value)> AcceptedValues =>
    [
        ("""{"values": ["draft", "live"]}""", "\"live\""),
        ("""{"values": ["only"]}""", "\"only\""),
    ];

    protected override IEnumerable<(string Settings, string Value)> RejectedValues =>
    [
        ("""{"values": ["draft", "live"]}""", "\"Live\""),
        ("""{"values": ["draft", "live"]}""", "\"other\""),
        ("""{"values": ["only"]}""", """["only"]"""),
    ];

    [Fact]
    public void Missing_values_are_reported_as_missing()
    {
        var error = Assert.Single(Registry.ValidateSettings(FieldTypeName, Parse("{}")));

        Assert.Equal(SchemaErrorCodes.SettingMissing, error.Code);
        Assert.Equal("The setting 'values' is required.", error.Message);
    }
}

public sealed class MediaFieldTypeTests : FieldTypeConformanceTests
{
    protected override string FieldTypeName => "media";

    protected override IEnumerable<string> AcceptedSettings => ["{}"];

    protected override IEnumerable<string> RejectedSettings => ["""{"accept": ["image/*"]}"""];

    protected override IEnumerable<(string Settings, string Value)> AcceptedValues =>
    [
        ("{}", "\"0199a000-0000-7000-8000-000000000001\""),
    ];

    protected override IEnumerable<(string Settings, string Value)> RejectedValues =>
    [
        ("{}", "\"hero.png\""),
        ("{}", "\"0199a000000070008000000000000001\""),
        ("{}", "1"),
    ];
}

public sealed class ReferenceFieldTypeTests : FieldTypeConformanceTests
{
    protected override string FieldTypeName => "reference";

    protected override IEnumerable<string> AcceptedSettings =>
        ["""{"target": "product"}""", """{"target": "product-page"}""", """{"target": "product", "field": "price"}"""];

    protected override IEnumerable<string> RejectedSettings =>
    [
        "{}",
        """{"target": "Product"}""",
        """{"target": "product page"}""",
        """{"target": ""}""",
        """{"target": 5}""",
        """{"field": "price"}""",
        """{"target": "product", "field": "Price"}""",
        """{"target": "product", "field": ""}""",
        """{"target": "product", "field": ["price"]}""",
    ];

    protected override IEnumerable<(string Settings, string Value)> AcceptedValues =>
    [
        ("""{"target": "product"}""", "\"0199a000-0000-7000-8000-000000000001\""),
        ("""{"target": "product", "field": "price"}""", "\"0199a000-0000-7000-8000-000000000001\""),
    ];

    protected override IEnumerable<(string Settings, string Value)> RejectedValues =>
    [
        ("""{"target": "product"}""", "\"product-1\""),
        ("""{"target": "product", "field": "price"}""", "19.99"),
        ("""{"target": "product"}""", """{"id": "0199a000-0000-7000-8000-000000000001"}"""),
    ];

    [Fact]
    public void A_value_that_is_not_an_id_names_what_it_should_point_at()
    {
        var entry = Assert.Single(Registry.ValidateValue(FieldTypeName, Parse("""{"target": "product"}"""), Parse("\"Widget\"")));
        var entity = Assert.Single(Registry.ValidateValue(FieldTypeName, Parse("""{"target": "product", "field": "price"}"""), Parse("19.99")));

        Assert.StartsWith("The value must be the id of an entry of the content type 'product', a UUID", entry.Message);
        Assert.StartsWith("The value must be the id of a 'product' entity, a UUID", entity.Message);
    }

    [Fact]
    public void Declares_its_target_type_and_field()
    {
        Assert.Equal([new ReferenceTarget("product")], Registry.ReferenceTargets(FieldTypeName, Parse("""{"target": "product"}""")));
        Assert.Equal(
            [new ReferenceTarget("product", "price")],
            Registry.ReferenceTargets(FieldTypeName, Parse("""{"target": "product", "field": "price"}""")));
    }
}

public sealed class ListFieldTypeTests : FieldTypeConformanceTests
{
    protected override string FieldTypeName => "list";

    protected override IEnumerable<string> AcceptedSettings =>
    [
        """{"itemType": "text"}""",
        """{"itemType": "text", "itemSettings": {"maxLength": 40}}""",
        """{"itemType": "enum", "itemSettings": {"values": ["a", "b"]}}""",
        """{"itemType": "reference", "itemSettings": {"target": "product"}}""",
    ];

    protected override IEnumerable<string> RejectedSettings =>
    [
        "{}",
        """{"itemType": "list", "itemSettings": {"itemType": "text"}}""",
        """{"itemType": "money"}""",
        """{"itemType": "enum"}""",
        """{"itemType": "text", "itemSettings": {"maxLength": 0}}""",
        """{"itemType": "text", "itemSettings": []}""",
    ];

    protected override IEnumerable<(string Settings, string Value)> AcceptedValues =>
    [
        ("""{"itemType": "text"}""", "[]"),
        ("""{"itemType": "text"}""", """["a", "b"]"""),
        ("""{"itemType": "enum", "itemSettings": {"values": ["a", "b"]}}""", """["b", "a", "b"]"""),
        ("""{"itemType": "reference", "itemSettings": {"target": "product"}}""", """["0199a000-0000-7000-8000-000000000001"]"""),
    ];

    protected override IEnumerable<(string Settings, string Value)> RejectedValues =>
    [
        ("""{"itemType": "text"}""", "\"a\""),
        ("""{"itemType": "text"}""", """["a", 5]"""),
        ("""{"itemType": "text"}""", "[null]"),
        ("""{"itemType": "text", "itemSettings": {"maxLength": 40}}""", """["xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"]"""),
        ("""{"itemType": "reference", "itemSettings": {"target": "product"}}""", """["product-1"]"""),
    ];

    [Fact]
    public void A_list_of_lists_is_rejected()
    {
        var error = Assert.Single(Registry.ValidateSettings(FieldTypeName, Parse("""{"itemType": "list"}""")));

        Assert.Equal(SchemaErrorCodes.SettingInvalid, error.Code);
        Assert.Contains("cannot be 'list'", error.Message);
    }

    [Fact]
    public void Declares_what_its_item_type_references()
    {
        Assert.Equal(
            [new ReferenceTarget("product", "price")],
            Registry.ReferenceTargets(FieldTypeName, Parse("""{"itemType": "reference", "itemSettings": {"target": "product", "field": "price"}}""")));
        Assert.Empty(Registry.ReferenceTargets(FieldTypeName, Parse("""{"itemType": "text"}""")));
    }

    [Fact]
    public void Each_item_that_is_wrong_is_named_by_its_position()
    {
        var errors = Registry.ValidateValue(FieldTypeName, Parse("""{"itemType": "text", "itemSettings": {"maxLength": 3}}"""), Parse("""["abc", 5, null, "abcd"]"""));

        Assert.Equal(
            [
                "Item 2: The value must be a string.",
                "Item 3: The value must not be null.",
                "Item 4: The value must be at most 3 characters, and it has 4.",
            ],
            errors.Select(error => error.Message));
    }

    [Fact]
    public void The_item_type_checks_the_item_settings()
    {
        var errors = Registry.ValidateSettings(FieldTypeName, Parse("""{"itemType": "enum", "itemSettings": {"values": [], "colour": "red"}}"""));

        Assert.Equal([SchemaErrorCodes.SettingInvalid, SchemaErrorCodes.SettingUnknown], errors.Select(error => error.Code));
        Assert.All(errors, error => Assert.StartsWith("In the list items: ", error.Message));
        Assert.Contains("'enum' field type has no setting 'colour'", errors[1].Message);
    }
}

public sealed class JsonFieldTypeTests : FieldTypeConformanceTests
{
    protected override string FieldTypeName => "json";

    protected override IEnumerable<string> AcceptedSettings => ["{}"];

    protected override IEnumerable<string> RejectedSettings => ["""{"schema": {}}"""];

    protected override IEnumerable<(string Settings, string Value)> AcceptedValues =>
    [
        ("{}", """{"key": "value"}"""),
        ("{}", """[1, "a", null]"""),
        ("{}", "\"text\""),
        ("{}", "0"),
    ];

    protected override IEnumerable<(string Settings, string Value)> RejectedValues => [];
}
