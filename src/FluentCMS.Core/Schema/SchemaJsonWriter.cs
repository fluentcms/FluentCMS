using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentCMS.Schema;

namespace FluentCMS.Core.Schema;

// The canonical form of a model, in the shape SchemaJsonReader reads. Defaults are left out, so
// equal models give equal text and an unchanged schema gives an unchanged file.
internal static class SchemaJsonWriter
{
    private static readonly JsonWriterOptions Options = new()
    {
        Indented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Write(SchemaModel model)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, Options))
        {
            writer.WriteStartObject();
            WriteTypes(writer, "collections", model.ContentTypes.Where(type => type.Kind == SchemaTypeKind.Collection));
            WriteTypes(writer, "singletons", model.ContentTypes.Where(type => type.Kind == SchemaTypeKind.Singleton));
            WriteTypes(writer, "components", model.Components);
            WriteTypes(writer, "blocks", model.Blocks);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteTypes(Utf8JsonWriter writer, string group, IEnumerable<SchemaType> types)
    {
        var list = types.ToList();
        if (list.Count == 0)
        {
            return;
        }

        writer.WriteStartArray(group);
        foreach (var type in list)
        {
            writer.WriteStartObject();
            writer.WriteString("name", type.Name);
            if (type.Localized)
            {
                writer.WriteBoolean("localized", true);
            }

            if (type.Scope == ContentScope.Global)
            {
                writer.WriteString("scope", "global");
            }

            writer.WriteStartArray("fields");
            foreach (var field in type.Fields)
            {
                WriteField(writer, field);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteField(Utf8JsonWriter writer, SchemaField field)
    {
        writer.WriteStartObject();
        writer.WriteString("name", field.Name);
        writer.WriteString("type", field.FieldType);
        WriteFlag(writer, "multiple", field.Multiple);
        WriteFlag(writer, "required", field.Required);
        WriteFlag(writer, "unique", field.Unique);
        WriteFlag(writer, "indexed", field.Indexed);
        WriteFlag(writer, "searchable", field.Searchable);
        WriteFlag(writer, "readOnly", field.ReadOnly);
        WriteFlag(writer, "hidden", field.Hidden);
        WriteFlag(writer, "localizable", field.Localizable);

        if (field.FormerNames.Count > 0)
        {
            WriteValue(writer, "formerNames", field.FormerNames);
        }

        if (field.Display is { } display)
        {
            writer.WriteStartObject("display");
            WriteText(writer, "label", display.Label);
            WriteText(writer, "help", display.Help);
            WriteText(writer, "placeholder", display.Placeholder);
            WriteText(writer, "group", display.Group);
            WriteText(writer, "tab", display.Tab);
            if (display.Width != 0)
            {
                writer.WriteNumber("width", display.Width);
            }

            writer.WriteEndObject();
        }

        if (field.DefaultValue is not null)
        {
            WriteValue(writer, "defaultValue", field.DefaultValue);
        }

        WriteNumber(writer, "minLength", field.MinLength);
        WriteNumber(writer, "maxLength", field.MaxLength);
        WriteNumber(writer, "minimum", field.Minimum);
        WriteNumber(writer, "maximum", field.Maximum);
        WriteText(writer, "pattern", field.Pattern);

        if (field.VisibleWhen is not null)
        {
            writer.WritePropertyName("visibleWhen");
            WriteCondition(writer, field.VisibleWhen);
        }

        if (field.RequiredWhen is not null)
        {
            writer.WritePropertyName("requiredWhen");
            WriteCondition(writer, field.RequiredWhen);
        }

        if (field.Relation is { } relation)
        {
            writer.WriteString("target", relation.Target);
            WriteText(writer, "reverse", relation.Reverse);
            WriteFlag(writer, "ordered", relation.Ordered);
        }

        foreach (var (key, value) in field.Settings)
        {
            WriteValue(writer, key, value);
        }

        writer.WriteEndObject();
    }

    internal static void WriteCondition(Utf8JsonWriter writer, Condition condition)
    {
        writer.WriteStartObject();
        switch (condition.Operator)
        {
            case ConditionOperator.Equal:
                writer.WriteString("field", condition.FieldName);
                WriteValue(writer, "equals", condition.Values[0]);
                break;
            case ConditionOperator.In:
                writer.WriteString("field", condition.FieldName);
                writer.WriteStartArray("in");
                foreach (var value in condition.Values)
                {
                    WriteValue(writer, null, value);
                }

                writer.WriteEndArray();
                break;
            case ConditionOperator.IsEmpty:
                writer.WriteString("field", condition.FieldName);
                writer.WriteBoolean("isEmpty", true);
                break;
            case ConditionOperator.Not:
                writer.WritePropertyName("not");
                WriteCondition(writer, condition.Operands[0]);
                break;
            default:
                writer.WriteStartArray(condition.Operator == ConditionOperator.And ? "and" : "or");
                foreach (var operand in condition.Operands)
                {
                    WriteCondition(writer, operand);
                }

                writer.WriteEndArray();
                break;
        }

        writer.WriteEndObject();
    }

    private static void WriteFlag(Utf8JsonWriter writer, string name, bool value)
    {
        if (value)
        {
            writer.WriteBoolean(name, true);
        }
    }

    private static void WriteText(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }

    private static void WriteNumber(Utf8JsonWriter writer, string name, int? value)
    {
        if (value is not null)
        {
            writer.WriteNumber(name, value.Value);
        }
    }

    private static void WriteNumber(Utf8JsonWriter writer, string name, decimal? value)
    {
        if (value is not null)
        {
            writer.WriteNumber(name, value.Value);
        }
    }

    // A null name writes an array element; a property name writes a property.
    private static void WriteValue(Utf8JsonWriter writer, string? name, object? value)
    {
        if (name is not null)
        {
            writer.WritePropertyName(name);
        }

        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case bool flag:
                writer.WriteBooleanValue(flag);
                break;
            case long whole:
                writer.WriteNumberValue(whole);
                break;
            case decimal number:
                writer.WriteNumberValue(number);
                break;
            case IEnumerable<string> list:
                writer.WriteStartArray();
                foreach (var item in list)
                {
                    writer.WriteStringValue(item);
                }

                writer.WriteEndArray();
                break;
            default:
                throw new InvalidOperationException($"A schema value of type {value.GetType().Name} cannot be written.");
        }
    }
}
