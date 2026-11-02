using System.Text.Json;
using FluentCMS.Schema;

namespace FluentCMS.Core.Schema;

// Reads a schema.json document by making the same builder calls the attribute reader makes. Problems in
// the document are collected on the builder, so one run reports all of them.
internal sealed class SchemaJsonReader(SchemaBuilder builder)
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private string _source = "";

    public void Read(string json, string source)
    {
        _source = source;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, DocumentOptions);
        }
        catch (JsonException exception)
        {
            Fail("", $"is not valid JSON: {exception.Message}");
            return;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                Fail("", "must be a JSON object");
                return;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "$schema":
                        break;
                    case "collections":
                        ReadTypes(property.Value, SchemaTypeKind.Collection, property.Name);
                        break;
                    case "singletons":
                        ReadTypes(property.Value, SchemaTypeKind.Singleton, property.Name);
                        break;
                    case "components":
                        ReadTypes(property.Value, SchemaTypeKind.Component, property.Name);
                        break;
                    case "blocks":
                        ReadTypes(property.Value, SchemaTypeKind.Block, property.Name);
                        break;
                    default:
                        Fail(property.Name, "is not a known property; use collections, singletons, components or blocks");
                        break;
                }
            }
        }
    }

    private bool Fail(string path, string message)
    {
        builder.Report(SchemaErrorCodes.InvalidSchemaFile, path.Length == 0 ? $"{_source} {message}" : $"{_source}: {path} {message}");
        return false;
    }

    private void ReadTypes(JsonElement element, SchemaTypeKind kind, string path)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            Fail(path, "must be an array");
            return;
        }

        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            ReadType(item, kind, $"{path}[{index++}]");
        }
    }

    private void ReadType(JsonElement element, SchemaTypeKind kind, string path)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            Fail(path, "must be an object");
            return;
        }

        string? name = null;
        var localized = false;
        var scope = ContentScope.Site;
        JsonElement? fields = null;
        var failed = false;

        foreach (var property in element.EnumerateObject())
        {
            var propertyPath = $"{path}.{property.Name}";
            switch (property.Name)
            {
                case "name":
                    failed |= !TryString(property.Value, propertyPath, out name);
                    break;
                case "localized":
                    failed |= !TryBool(property.Value, propertyPath, out localized);
                    break;
                case "scope":
                    failed |= !TryScope(property.Value, kind, propertyPath, out scope);
                    break;
                case "fields":
                    fields = property.Value;
                    break;
                default:
                    Fail(propertyPath, "is not a known property of a type");
                    failed = true;
                    break;
            }
        }

        if (name is null)
        {
            if (!failed)
            {
                Fail(path, "needs a name");
            }

            return;
        }

        builder.AddType(name, kind, null, type =>
        {
            type.Localized(localized);
            type.Scope(scope);
            if (fields is { } list)
            {
                ReadFields(type, list, $"{path}.fields");
            }
        });
    }

    private bool TryScope(JsonElement element, SchemaTypeKind kind, string path, out ContentScope scope)
    {
        scope = ContentScope.Site;
        if (kind is not (SchemaTypeKind.Collection or SchemaTypeKind.Singleton))
        {
            return Fail(path, "is only allowed on collections and singletons");
        }

        switch (element.ValueKind == JsonValueKind.String ? element.GetString() : null)
        {
            case "site":
                return true;
            case "global":
                scope = ContentScope.Global;
                return true;
            default:
                return Fail(path, "must be \"site\" or \"global\"");
        }
    }

    private void ReadFields(ITypeBuilder type, JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            Fail(path, "must be an array");
            return;
        }

        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            ReadField(type, item, $"{path}[{index++}]");
        }
    }

    private void ReadField(ITypeBuilder type, JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            Fail(path, "must be an object");
            return;
        }

        string? name = null;
        string? fieldType = null;
        var failed = false;
        List<Action<IFieldBuilder>> steps = [];
        int? minLength = null, maxLength = null;
        decimal? minimum = null, maximum = null;
        string? target = null, reverse = null;
        var ordered = false;

        foreach (var property in element.EnumerateObject())
        {
            var propertyPath = $"{path}.{property.Name}";
            var value = property.Value;
            var ok = true;

            switch (property.Name)
            {
                case "name":
                    ok = TryString(value, propertyPath, out name);
                    break;
                case "type":
                    ok = TryString(value, propertyPath, out fieldType);
                    break;
                case "multiple":
                    ok = TryFlag(value, propertyPath, steps, (f, flag) => f.Multiple(flag));
                    break;
                case "required":
                    ok = TryFlag(value, propertyPath, steps, (f, flag) => f.Required(flag));
                    break;
                case "unique":
                    ok = TryFlag(value, propertyPath, steps, (f, flag) => f.Unique(flag));
                    break;
                case "indexed":
                    ok = TryFlag(value, propertyPath, steps, (f, flag) => f.Indexed(flag));
                    break;
                case "searchable":
                    ok = TryFlag(value, propertyPath, steps, (f, flag) => f.Searchable(flag));
                    break;
                case "readOnly":
                    ok = TryFlag(value, propertyPath, steps, (f, flag) => f.IsReadOnly(flag));
                    break;
                case "hidden":
                    ok = TryFlag(value, propertyPath, steps, (f, flag) => f.Hidden(flag));
                    break;
                case "localizable":
                    ok = TryFlag(value, propertyPath, steps, (f, flag) => f.Localizable(flag));
                    break;
                case "ordered":
                    ok = TryBool(value, propertyPath, out ordered);
                    break;
                case "formerNames":
                    ok = TryValue(value, propertyPath, allowNull: false, out var names)
                        && (names is IReadOnlyList<string> list
                            ? Add(steps, f => f.FormerNames([.. list]))
                            : Fail(propertyPath, "must be an array of strings"));
                    break;
                case "display":
                    ok = ReadDisplay(value, propertyPath, steps);
                    break;
                case "defaultValue":
                    ok = TryValue(value, propertyPath, allowNull: true, out var defaultValue)
                        && Add(steps, f => f.DefaultValue(defaultValue));
                    break;
                case "minLength":
                    ok = TryInt(value, propertyPath, out var min);
                    minLength = ok ? min : null;
                    break;
                case "maxLength":
                    ok = TryInt(value, propertyPath, out var max);
                    maxLength = ok ? max : null;
                    break;
                case "minimum":
                    ok = TryDecimal(value, propertyPath, out var least);
                    minimum = ok ? least : null;
                    break;
                case "maximum":
                    ok = TryDecimal(value, propertyPath, out var greatest);
                    maximum = ok ? greatest : null;
                    break;
                case "pattern":
                    ok = TryString(value, propertyPath, out var pattern) && Add(steps, f => f.Pattern(pattern!));
                    break;
                case "visibleWhen":
                    ok = ReadCondition(value, propertyPath) is { } visible && Add(steps, f => f.VisibleWhen(visible));
                    break;
                case "requiredWhen":
                    ok = ReadCondition(value, propertyPath) is { } required && Add(steps, f => f.RequiredWhen(required));
                    break;
                case "target":
                    ok = TryString(value, propertyPath, out target);
                    break;
                case "reverse":
                    ok = TryString(value, propertyPath, out reverse);
                    break;
                default:
                    ok = TryValue(value, propertyPath, allowNull: false, out var setting)
                        && Add(steps, f => f.Setting(property.Name, setting!));
                    break;
            }

            failed |= !ok;
        }

        if (name is null || fieldType is null)
        {
            if (!failed)
            {
                Fail(path, "needs a name and a type");
            }

            return;
        }

        if (target is null && (reverse is not null || ordered))
        {
            Fail(path, "sets reverse or ordered without a target");
            return;
        }

        if (failed)
        {
            return;
        }

        type.Field(name, fieldType, field =>
        {
            foreach (var step in steps)
            {
                step(field);
            }

            if (minLength is not null || maxLength is not null)
            {
                field.Length(minLength, maxLength);
            }

            if (minimum is not null || maximum is not null)
            {
                field.Range(minimum, maximum);
            }

            if (target is not null)
            {
                field.Relation(target, reverse, ordered);
            }
        });
    }

    private static bool Add(List<Action<IFieldBuilder>> steps, Action<IFieldBuilder> step)
    {
        steps.Add(step);
        return true;
    }

    private bool TryFlag(JsonElement element, string path, List<Action<IFieldBuilder>> steps, Action<IFieldBuilder, bool> apply)
    {
        if (!TryBool(element, path, out var flag))
        {
            return false;
        }

        steps.Add(field => apply(field, flag));
        return true;
    }

    private bool ReadDisplay(JsonElement element, string path, List<Action<IFieldBuilder>> steps)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return Fail(path, "must be an object");
        }

        string? label = null, help = null, placeholder = null, group = null, tab = null;
        var width = 0;
        var ok = true;

        foreach (var property in element.EnumerateObject())
        {
            var propertyPath = $"{path}.{property.Name}";
            ok &= property.Name switch
            {
                "label" => TryString(property.Value, propertyPath, out label),
                "help" => TryString(property.Value, propertyPath, out help),
                "placeholder" => TryString(property.Value, propertyPath, out placeholder),
                "group" => TryString(property.Value, propertyPath, out group),
                "tab" => TryString(property.Value, propertyPath, out tab),
                "width" => TryInt(property.Value, propertyPath, out width),
                _ => Fail(propertyPath, "is not a known display property"),
            };
        }

        return ok && Add(steps, f => f.Display(label, help, placeholder, group, tab, width));
    }

    internal Condition? ReadCondition(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            Fail(path, "must be an object");
            return null;
        }

        string? field = null;
        JsonElement? equalsValue = null, inValues = null, emptyFlag = null, allOf = null, anyOf = null, negation = null;

        foreach (var property in element.EnumerateObject())
        {
            switch (property.Name)
            {
                case "field":
                    if (!TryString(property.Value, $"{path}.field", out field))
                    {
                        return null;
                    }

                    break;
                case "equals":
                    equalsValue = property.Value;
                    break;
                case "in":
                    inValues = property.Value;
                    break;
                case "isEmpty":
                    emptyFlag = property.Value;
                    break;
                case "and":
                    allOf = property.Value;
                    break;
                case "or":
                    anyOf = property.Value;
                    break;
                case "not":
                    negation = property.Value;
                    break;
                default:
                    Fail($"{path}.{property.Name}", "is not a known condition property");
                    return null;
            }
        }

        if (new[] { equalsValue, inValues, emptyFlag, allOf, anyOf, negation }.Count(candidate => candidate is not null) != 1)
        {
            Fail(path, "needs exactly one of equals, in, isEmpty, and, or, not");
            return null;
        }

        if (negation is { } negated)
        {
            return ReadCondition(negated, $"{path}.not")?.Not();
        }

        if (allOf is { } all)
        {
            return ReadConditions(all, $"{path}.and") is { } operands ? Condition.Combine(ConditionOperator.And, operands) : null;
        }

        if (anyOf is { } any)
        {
            return ReadConditions(any, $"{path}.or") is { } operands ? Condition.Combine(ConditionOperator.Or, operands) : null;
        }

        if (string.IsNullOrEmpty(field))
        {
            Fail(path, "needs a field");
            return null;
        }

        if (emptyFlag is { } empty)
        {
            if (empty.ValueKind == JsonValueKind.True)
            {
                return Condition.Field(field).IsEmpty();
            }

            Fail($"{path}.isEmpty", "must be true");
            return null;
        }

        if (equalsValue is { } single)
        {
            if (!TryValue(single, $"{path}.equals", allowNull: true, out var value))
            {
                return null;
            }

            if (value is IReadOnlyList<string>)
            {
                Fail($"{path}.equals", "must be a string, boolean, number or null");
                return null;
            }

            return Condition.Field(field).Equals(value);
        }

        return ReadMembers(inValues!.Value, $"{path}.in") is { } members ? Condition.Field(field).In(members) : null;
    }

    private object?[]? ReadMembers(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            Fail(path, "must be an array");
            return null;
        }

        List<object?> members = [];
        foreach (var item in element.EnumerateArray())
        {
            var itemPath = $"{path}[{members.Count}]";
            if (!TryValue(item, itemPath, allowNull: true, out var value))
            {
                return null;
            }

            if (value is IReadOnlyList<string>)
            {
                Fail(itemPath, "must be a string, boolean, number or null");
                return null;
            }

            members.Add(value);
        }

        return [.. members];
    }

    private List<Condition>? ReadConditions(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() == 0)
        {
            Fail(path, "must be a non-empty array of conditions");
            return null;
        }

        List<Condition> conditions = [];
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            if (ReadCondition(item, $"{path}[{index++}]") is not { } condition)
            {
                return null;
            }

            conditions.Add(condition);
        }

        return conditions;
    }

    private bool TryString(JsonElement element, string path, out string? value)
    {
        value = element.ValueKind == JsonValueKind.String ? element.GetString() : null;
        return value is not null || Fail(path, "must be a string");
    }

    private bool TryBool(JsonElement element, string path, out bool value)
    {
        value = element.ValueKind == JsonValueKind.True;
        return element.ValueKind is JsonValueKind.True or JsonValueKind.False || Fail(path, "must be true or false");
    }

    private bool TryInt(JsonElement element, string path, out int value)
    {
        value = 0;
        return (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value)) || Fail(path, "must be a whole number");
    }

    private bool TryDecimal(JsonElement element, string path, out decimal value)
    {
        value = 0;
        return (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out value)) || Fail(path, "must be a number");
    }

    // Reads the values a schema holds: a string, boolean, number or, when it is a list, an array of strings.
    private bool TryValue(JsonElement element, string path, bool allowNull, out object? value)
    {
        value = null;
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                value = element.GetString();
                return true;
            case JsonValueKind.True:
            case JsonValueKind.False:
                value = element.GetBoolean();
                return true;
            case JsonValueKind.Null when allowNull:
                return true;
            case JsonValueKind.Number when element.TryGetInt64(out var whole):
                value = whole;
                return true;
            case JsonValueKind.Number when element.TryGetDecimal(out var number):
                value = SchemaValue.Normalize(number);
                return true;
            case JsonValueKind.Array:
                var items = element.EnumerateArray().ToList();
                if (items.All(item => item.ValueKind == JsonValueKind.String))
                {
                    value = items.Select(item => item.GetString()!).ToList().AsReadOnly();
                    return true;
                }

                break;
        }

        return Fail(path, "must be a string, boolean, number or array of strings");
    }
}
