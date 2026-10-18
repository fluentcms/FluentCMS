using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Builtin.FieldTypes;

// Points at an entry of the content type named by 'target', or with 'field' at one field of it, such as product.price (SCH-04).
// Whether the type and field exist, and that a field target is an entity type, is checked where types are stored.
internal sealed class ReferenceFieldType : IFieldType
{
    public string Name => "reference";

    public IReadOnlyList<Error> ValidateSettings(JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        var reader = new FieldSettingsReader(Name, settings);
        var target = reader.RequiredString("target");
        var field = reader.OptionalString("field");

        if (target is not null && !SchemaNames.IsSlug(target))
        {
            reader.Invalid("target", "must be the slug of a content type, such as 'product'");
        }

        if (field is not null && !SchemaNames.IsFieldName(field))
        {
            reader.Invalid("field", "must be the name of a field of the target, such as 'price'");
        }

        return reader.Errors();
    }

    public FieldValueDescription DescribeValue(JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        var target = ReferenceTargets(settings, fieldTypes)[0];

        // A field reference stores the entity, not the value, so the page always shows the entity's current fact (SCH-04, CNT-04).
        return new FieldValueDescription(
            new JsonObject { ["type"] = "string", ["format"] = "uuid" },
            FieldValueDescription.ExampleId,
            target.Field is null
                ? $"The id of an entry of the content type '{target.Type}'."
                : $"The id of a '{target.Type}' entity; the field shows that entity's '{target.Field}' instead of a copy of it.");
    }

    public IReadOnlyList<ReferenceTarget> ReferenceTargets(JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        var field = settings.TryGetProperty("field", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        return [new ReferenceTarget(settings.GetProperty("target").GetString()!, field)];
    }
}
