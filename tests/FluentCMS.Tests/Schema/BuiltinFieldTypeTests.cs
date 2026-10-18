using FluentCMS.Core.Schema;

namespace FluentCMS.Tests.Schema;

public sealed class TextFieldTypeTests : FieldTypeConformanceTests
{
    protected override string FieldTypeName => "text";

    protected override IEnumerable<string> AcceptedSettings => ["{}", """{"maxLength": 200}"""];

    protected override IEnumerable<string> RejectedSettings =>
        ["""{"maxLength": 0}""", """{"maxLength": -5}""", """{"maxLength": 1.5}""", """{"maxLength": "200"}"""];
}

public sealed class RichTextFieldTypeTests : FieldTypeConformanceTests
{
    protected override string FieldTypeName => "richText";

    protected override IEnumerable<string> AcceptedSettings => ["{}", """{"maxLength": 20000}"""];

    protected override IEnumerable<string> RejectedSettings => ["""{"maxLength": 0}""", """{"maxLength": true}"""];
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

    [Fact]
    public void A_maximum_below_the_minimum_is_reported_on_max()
    {
        var error = Assert.Single(Registry.ValidateSettings(FieldTypeName, Parse("""{"min": 5, "max": 1}""")));

        Assert.Equal(SchemaErrorCodes.SettingInvalid, error.Code);
        Assert.Equal("The setting 'max' must not be less than 'min'.", error.Message);
    }
}

public sealed class BooleanFieldTypeTests : FieldTypeConformanceTests
{
    protected override string FieldTypeName => "boolean";

    protected override IEnumerable<string> AcceptedSettings => ["{}"];

    protected override IEnumerable<string> RejectedSettings => ["""{"default": true}"""];
}

public sealed class DateTimeFieldTypeTests : FieldTypeConformanceTests
{
    protected override string FieldTypeName => "dateTime";

    protected override IEnumerable<string> AcceptedSettings => ["{}", """{"dateOnly": true}""", """{"dateOnly": false}"""];

    protected override IEnumerable<string> RejectedSettings => ["""{"dateOnly": "yes"}""", """{"dateOnly": 1}"""];
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
}

public sealed class ReferenceFieldTypeTests : FieldTypeConformanceTests
{
    protected override string FieldTypeName => "reference";

    protected override IEnumerable<string> AcceptedSettings => ["""{"target": "product"}""", """{"target": "product-page"}"""];

    protected override IEnumerable<string> RejectedSettings =>
        ["{}", """{"target": "Product"}""", """{"target": "product page"}""", """{"target": ""}""", """{"target": 5}"""];
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

    [Fact]
    public void A_list_of_lists_is_rejected()
    {
        var error = Assert.Single(Registry.ValidateSettings(FieldTypeName, Parse("""{"itemType": "list"}""")));

        Assert.Equal(SchemaErrorCodes.SettingInvalid, error.Code);
        Assert.Contains("cannot be 'list'", error.Message);
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
}
