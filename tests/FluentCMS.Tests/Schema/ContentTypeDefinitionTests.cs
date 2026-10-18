using System.Text.Json;
using FluentCMS.Core;
using FluentCMS.Core.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Tests.Schema;

public sealed class ContentTypeDefinitionTests : IDisposable
{
    // One field of every SCH-02 type, in the form the definition is stored in.
    private const string EveryFieldType =
        """
        {"fields":[
          {"name":"title","type":"text","required":true,"settings":{"maxLength":200}},
          {"name":"body","type":"richText","required":false,"settings":{}},
          {"name":"price","type":"number","required":true,"settings":{"min":0,"max":99999.99}},
          {"name":"inStock","type":"boolean","required":false,"settings":{}},
          {"name":"releasedOn","type":"dateTime","required":false,"settings":{"dateOnly":true}},
          {"name":"status","type":"enum","required":true,"settings":{"values":["draft","live","retired"]}},
          {"name":"hero","type":"media","required":false,"settings":{}},
          {"name":"category","type":"reference","required":true,"settings":{"target":"category"}},
          {"name":"tags","type":"list","required":false,"settings":{"itemType":"text","itemSettings":{"maxLength":40}}},
          {"name":"specs","type":"json","required":false,"settings":{}}
        ]}
        """;

    private readonly ServiceProvider _provider = new ServiceCollection().AddFluentCmsCore().BuildServiceProvider();

    private FieldTypeRegistry FieldTypes => _provider.GetRequiredService<FieldTypeRegistry>();

    [Fact]
    public void Every_field_type_is_declared_validated_and_read_back_from_JSON()
    {
        var parsed = ContentTypeDefinition.Parse(EveryFieldType);

        Assert.True(parsed.IsSuccess, string.Join("; ", parsed.Errors.Select(error => error.Message)));
        var definition = parsed.Value;
        Assert.True(definition.Validate(FieldTypes).IsSuccess);
        Assert.Equal(FieldTypes.Names, definition.Fields.Select(field => field.Type).Order(StringComparer.Ordinal));

        var json = definition.ToJson();
        var readBack = ContentTypeDefinition.Parse(json).Value;

        Assert.True(JsonElement.DeepEquals(Element(EveryFieldType), Element(json)));
        Assert.Equal(json, readBack.ToJson());
        Assert.Equal(definition.Fields.Select(Describe), readBack.Fields.Select(Describe));
    }

    [Fact]
    public void ToJson_writes_every_property_of_every_field()
    {
        var definition = new ContentTypeDefinition(
        [
            new FieldDefinition("title", "text", required: true, settings: Element("""{"maxLength": 200}""")),
            new FieldDefinition("body", "richText"),
        ]);

        Assert.Equal(
            """{"fields":[{"name":"title","type":"text","required":true,"settings":{"maxLength":200}},{"name":"body","type":"richText","required":false,"settings":{}}]}""",
            definition.ToJson());
    }

    [Fact]
    public void Required_and_settings_may_be_left_out()
    {
        var field = Assert.Single(ContentTypeDefinition.Parse("""{"fields":[{"name":"title","type":"text"}]}""").Value.Fields);

        Assert.False(field.Required);
        Assert.Equal(JsonValueKind.Object, field.Settings.ValueKind);
        Assert.Empty(field.Settings.EnumerateObject());
    }

    [Fact]
    public void A_definition_may_start_without_fields()
    {
        var definition = ContentTypeDefinition.Parse("""{"fields":[]}""").Value;

        Assert.Empty(definition.Fields);
        Assert.True(definition.Validate(FieldTypes).IsSuccess);
    }

    [Theory]
    [InlineData("""{"fields": [""")]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("""{"fields": [], "fields": []}""")]
    public void Malformed_JSON_is_rejected_with_its_position(string json)
    {
        var error = Assert.Single(ContentTypeDefinition.Parse(json).Errors);

        Assert.Equal(SchemaErrorCodes.DefinitionMalformed, error.Code);
        Assert.StartsWith("The definition is not valid JSON: ", error.Message);
    }

    [Theory]
    [InlineData("[]", "$ must be an object with a 'fields' array.")]
    [InlineData("{}", "$.fields must be an array.")]
    [InlineData("""{"fields": {}}""", "$.fields must be an array.")]
    public void The_document_is_an_object_with_a_fields_array(string json, string message)
    {
        var error = Assert.Single(ContentTypeDefinition.Parse(json).Errors);

        Assert.Equal(SchemaErrorCodes.DefinitionMalformed, error.Code);
        Assert.Equal(message, error.Message);
    }

    [Fact]
    public void Every_structural_problem_is_reported_with_its_path()
    {
        const string json =
            """
            {"version": 2, "fields": [
              {"name": "a", "type": 5},
              {"type": "text"},
              "title",
              {"name": "b", "type": "text", "required": "yes", "settings": [], "label": "B"},
              {"name": null}
            ]}
            """;

        var result = ContentTypeDefinition.Parse(json);

        Assert.False(result.IsSuccess);
        Assert.All(result.Errors, error => Assert.Equal(SchemaErrorCodes.DefinitionMalformed, error.Code));
        Assert.Equal(
        [
            "$.version is not part of a definition; it has only 'fields'.",
            "$.fields[0].type must be a string.",
            "$.fields[1].name is missing.",
            "$.fields[2] must be an object.",
            "$.fields[3].required must be true or false.",
            "$.fields[3].settings must be an object.",
            "$.fields[3].label is not part of a field; a field has name, type, required and settings.",
            "$.fields[4].name must be a string.",
            "$.fields[4].type is missing.",
        ],
            result.Errors.Select(error => error.Message));
    }

    [Fact]
    public void Validation_reports_every_problem_and_names_its_field()
    {
        var definition = new ContentTypeDefinition(
        [
            new FieldDefinition("1st", "text"),
            new FieldDefinition("price", "number", settings: Element("""{"min": 5, "max": 1}""")),
            new FieldDefinition("validFrom", "dateTime"),
            new FieldDefinition("validfrom", "dateTime"),
            new FieldDefinition("status", "money"),
            new FieldDefinition("tags", "list", settings: Element("""{"itemType": "list"}""")),
            new FieldDefinition("kind", "enum"),
        ]);

        var result = definition.Validate(FieldTypes);

        Assert.Equal(
        [
            SchemaErrorCodes.FieldNameInvalid,
            SchemaErrorCodes.SettingInvalid,
            SchemaErrorCodes.FieldNameDuplicate,
            SchemaErrorCodes.FieldTypeUnknown,
            SchemaErrorCodes.SettingInvalid,
            SchemaErrorCodes.SettingMissing,
        ],
            result.Errors.Select(error => error.Code));
        Assert.Equal(
        [
            "Field 1: the name '1st' must start with a lowercase letter, contain only letters, digits and underscores, and be at most 64 characters.",
            "Field 'price': The setting 'max' must not be less than 'min'.",
            "Field 'validfrom': another field already has this name; field names are compared ignoring case.",
            "Field 'status': There is no field type 'money'; the known types are boolean, dateTime, enum, json, list, media, number, reference, richText, text.",
            "Field 'tags': The setting 'itemType' cannot be 'list'; use a 'json' field for nested structures.",
            "Field 'kind': The setting 'values' is required.",
        ],
            result.Errors.Select(error => error.Message));
    }

    [Fact]
    public void References_list_each_field_that_points_at_a_content_type()
    {
        var definition = ContentTypeDefinition.Parse(EveryFieldType).Value;

        Assert.Equal([new FieldReference("category", "category")], definition.References(FieldTypes));
    }

    [Fact]
    public void Field_settings_must_be_an_object()
    {
        Assert.Throws<ArgumentException>(() => new FieldDefinition("title", "text", settings: Element("[]")));
    }

    public void Dispose()
    {
        _provider.Dispose();
    }

    private static JsonElement Element(string json)
    {
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    private static string Describe(FieldDefinition field)
    {
        return $"{field.Name}|{field.Type}|{field.Required}|{field.Settings.GetRawText()}";
    }
}
