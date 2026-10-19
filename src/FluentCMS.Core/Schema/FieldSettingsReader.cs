using System.Text.Json;
using FluentCMS.Core.Extensions;

namespace FluentCMS.Core.Schema;

// Reads the settings of one field for its field type; SettingsReader does the bookkeeping, so built-ins and plugins word errors alike.
public sealed class FieldSettingsReader(string fieldType, JsonElement settings)
    : SettingsReader($"The '{Required(fieldType)}' field type", settings)
{
    private static string Required(string fieldType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldType);
        return fieldType;
    }
}
