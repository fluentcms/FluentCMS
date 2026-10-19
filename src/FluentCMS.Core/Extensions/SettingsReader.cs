using System.Text.Json;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Extensions;

// Reads the settings object an extension is configured with and collects what is wrong, so built-ins and plugins word errors alike.
// Every setting the extension asks for counts as known; anything else in the object is reported as unknown, so a typo an agent makes
// comes back as an error instead of being ignored (UX-03). An extension therefore reads all of its settings before calling Errors().
public abstract class SettingsReader
{
    private readonly string _owner;
    private readonly JsonElement _settings;
    private readonly List<string> _known = [];
    private readonly List<Error> _errors = [];

    // The owner reads as the subject of a sentence, such as "The 'text' field type".
    protected SettingsReader(string owner, JsonElement settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        _owner = owner;
        _settings = settings;
    }

    public int? PositiveInteger(string name)
    {
        if (Read(name) is not { } value)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number > 0)
        {
            return number;
        }

        Invalid(name, "must be a whole number greater than zero");
        return null;
    }

    public decimal? Number(string name)
    {
        if (Read(name) is not { } value)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        Invalid(name, "must be a number");
        return null;
    }

    public bool Flag(string name)
    {
        if (Read(name) is not { } value)
        {
            return false;
        }

        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        Invalid(name, "must be true or false");
        return false;
    }

    public string? RequiredString(string name)
    {
        if (Read(name) is not { } value)
        {
            Missing(name);
            return null;
        }

        if (value.ValueKind == JsonValueKind.String && value.GetString() is { } text && !string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        Invalid(name, "must be a non-empty string");
        return null;
    }

    public string? OptionalString(string name)
    {
        if (Read(name) is not { } value)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String && value.GetString() is { } text && !string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        Invalid(name, "must be a non-empty string");
        return null;
    }

    public IReadOnlyList<string>? RequiredStringList(string name)
    {
        if (Read(name) is not { } value)
        {
            Missing(name);
            return null;
        }

        return StringList(name, value);
    }

    public IReadOnlyList<string>? OptionalStringList(string name)
    {
        return Read(name) is { } value ? StringList(name, value) : null;
    }

    public JsonElement? OptionalObject(string name)
    {
        if (Read(name) is not { } value)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            return value;
        }

        Invalid(name, "must be a JSON object");
        return null;
    }

    public void Invalid(string name, string requirement)
    {
        _errors.Add(new Error(SchemaErrorCodes.SettingInvalid, $"The setting '{name}' {requirement}."));
    }

    public void Add(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        _errors.Add(error);
    }

    public IReadOnlyList<Error> Errors()
    {
        if (_settings.ValueKind != JsonValueKind.Object)
        {
            return [new Error(SchemaErrorCodes.SettingInvalid, "The settings must be a JSON object.")];
        }

        var unknown = _settings.EnumerateObject()
            .Select(property => property.Name)
            .Where(name => !_known.Contains(name))
            .Distinct()
            .Select(name => new Error(SchemaErrorCodes.SettingUnknown, _known.Count == 0
                ? $"{_owner} takes no settings, so '{name}' is not one."
                : $"{_owner} has no setting '{name}'; its settings are {string.Join(", ", _known)}."));

        return [.. _errors, .. unknown];
    }

    private IReadOnlyList<string>? StringList(string name, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            var items = value.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null)
                .ToList();

            if (items.Count > 0
                && items.All(item => !string.IsNullOrWhiteSpace(item))
                && items.Distinct(StringComparer.Ordinal).Count() == items.Count)
            {
                return items!;
            }
        }

        Invalid(name, "must be a non-empty array of distinct, non-empty strings");
        return null;
    }

    private void Missing(string name)
    {
        _errors.Add(new Error(SchemaErrorCodes.SettingMissing, $"The setting '{name}' is required."));
    }

    // An explicit null reads as an absent setting.
    private JsonElement? Read(string name)
    {
        if (!_known.Contains(name))
        {
            _known.Add(name);
        }

        return _settings.ValueKind == JsonValueKind.Object
            && _settings.TryGetProperty(name, out var value)
            && value.ValueKind != JsonValueKind.Null
                ? value
                : null;
    }
}
