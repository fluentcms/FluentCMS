using System.Text.Json;
using FluentCMS.Core.Extensions;

namespace FluentCMS.Core.Checks;

// Reads the settings a check gives its check type, worded like field settings (SettingsReader).
public sealed class CheckSettingsReader(string checkType, JsonElement settings)
    : SettingsReader($"The '{Required(checkType)}' check type", settings)
{
    private static string Required(string checkType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checkType);
        return checkType;
    }
}
