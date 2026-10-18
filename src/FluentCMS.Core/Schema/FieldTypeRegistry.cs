using System.Text.Json;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Schema;

// Every field type registered through the extension point, by name. Built-ins and plugins arrive the same way (EXT-N1).
public sealed class FieldTypeRegistry
{
    private readonly Dictionary<string, IFieldType> _byName = new(StringComparer.Ordinal);

    public FieldTypeRegistry(IEnumerable<IFieldType> fieldTypes)
    {
        ArgumentNullException.ThrowIfNull(fieldTypes);

        // A name is stored in every definition that uses it, so a bad or duplicate one stops the application from starting.
        foreach (var fieldType in fieldTypes)
        {
            if (!SchemaNames.IsFieldTypeName(fieldType.Name))
            {
                throw new InvalidOperationException(
                    $"{fieldType.GetType()} is named '{fieldType.Name}', which is not a valid field type name.");
            }

            if (!_byName.TryAdd(fieldType.Name, fieldType))
            {
                throw new InvalidOperationException(
                    $"{fieldType.GetType()} and {_byName[fieldType.Name].GetType()} are both named '{fieldType.Name}'.");
            }
        }
    }

    public IReadOnlyList<string> Names => _byName.Keys.Order(StringComparer.Ordinal).ToList();

    public IFieldType? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _byName.GetValueOrDefault(name);
    }

    public IReadOnlyList<Error> ValidateSettings(string type, JsonElement settings)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (Find(type) is not { } fieldType)
        {
            return [new Error(
                SchemaErrorCodes.FieldTypeUnknown,
                $"There is no field type '{type}'; the known types are {string.Join(", ", Names)}.")];
        }

        if (settings.ValueKind != JsonValueKind.Object)
        {
            return [new Error(SchemaErrorCodes.SettingInvalid, "The settings must be a JSON object.")];
        }

        return fieldType.ValidateSettings(settings, this);
    }

    public IReadOnlyList<string> ReferencedTypes(string type, JsonElement settings)
    {
        ArgumentNullException.ThrowIfNull(type);

        return Find(type)?.ReferencedTypes(settings, this) ?? [];
    }
}
