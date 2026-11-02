using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Schema;

namespace FluentCMS.Core.Schema.FieldTypes;

// The types whose value is a string; each adds its own check of the text.
internal abstract class StringFieldType(string name, Type[] clrTypes, FilterOperator[] filterOperators, bool sortable, bool indexable, params FieldSettingDescriptor[] settings)
    : CoreFieldType(name, clrTypes, filterOperators, sortable, indexable, settings)
{
    public override bool TryCoerce(object? value, SchemaField field, out object? result, out string? message)
    {
        var raw = RawValue.Unwrap(value);
        string text;
        switch (raw)
        {
            case null:
                return Ok(null, out result, out message);
            case string s:
                text = s;
                break;
            case char c:
                text = c.ToString();
                break;
            default:
                return Expected("a string", raw, out result, out message);
        }

        return Check(text) is { } problem ? Fail(problem, out result, out message) : Ok(text, out result, out message);
    }

    public override JsonObject OpenApiSchema(SchemaField field)
    {
        var schema = new JsonObject { ["type"] = "string" };
        Describe(schema);

        if (field.MinLength is { } min)
        {
            schema["minLength"] = min;
        }

        if (field.MaxLength is { } max)
        {
            schema["maxLength"] = max;
        }

        if (field.Pattern is { } pattern && !schema.ContainsKey("pattern"))
        {
            schema["pattern"] = pattern;
        }

        return schema;
    }

    protected virtual string? Check(string text) => null;

    protected virtual void Describe(JsonObject schema)
    {
    }
}
