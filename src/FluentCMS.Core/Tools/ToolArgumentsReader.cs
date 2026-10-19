using System.Text.Json;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Tools;

// Reads a tool's arguments and collects what is wrong, by argument name, so every tool words its errors alike. Every argument a
// tool asks for counts as known; anything else is reported as unknown, so an agent's typo comes back as an error instead of
// being ignored (UX-03), as FieldSettingsReader does for field settings. A tool reads all its arguments before calling Errors().
public sealed class ToolArgumentsReader
{
    private readonly JsonElement _arguments;
    private readonly List<string> _known = [];
    private readonly List<Error> _errors = [];

    public ToolArgumentsReader(JsonElement arguments)
    {
        _arguments = arguments;

        if (arguments.ValueKind is not (JsonValueKind.Object or JsonValueKind.Undefined or JsonValueKind.Null))
        {
            _errors.Add(new Error(ToolErrorCodes.ArgumentsNotObject, "The arguments must be a JSON object with one property per argument."));
        }
    }

    public string? RequiredString(string name, string example)
    {
        return String(name, example, required: true);
    }

    public string? OptionalString(string name, string example)
    {
        return String(name, example, required: false);
    }

    public IReadOnlyList<string>? OptionalStrings(string name, string example)
    {
        if (Read(name) is not { } value)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Array
            && value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString())))
        {
            return [.. value.EnumerateArray().Select(item => item.GetString()!)];
        }

        Invalid(name, $"must be an array of non-empty strings such as [\"{example}\"]");
        return null;
    }

    public JsonElement? RequiredObject(string name, string form)
    {
        if (Read(name) is not { } value)
        {
            Missing(name, form);
            return null;
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            return value;
        }

        Invalid(name, $"must be a JSON object: {form}");
        return null;
    }

    // Any JSON value but null, such as the value of a field.
    public JsonElement? RequiredValue(string name, string form)
    {
        var value = Read(name);
        if (value is null)
        {
            Missing(name, form);
        }

        return value;
    }

    public JsonElement? OptionalObject(string name, string form)
    {
        if (Read(name) is not { } value)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            return value;
        }

        Invalid(name, $"must be a JSON object: {form}");
        return null;
    }

    public bool? OptionalFlag(string name)
    {
        if (Read(name) is not { } value)
        {
            return null;
        }

        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        Invalid(name, "must be true or false");
        return null;
    }

    public Guid? OptionalGuid(string name)
    {
        return Read(name) is null ? null : GuidOf(name);
    }

    public Guid? RequiredGuid(string name)
    {
        if (Read(name) is null)
        {
            Missing(name, "a UUID such as '0192f3a1-7c2e-7a4b-9d3e-1f2a3b4c5d6e'");
            return null;
        }

        return GuidOf(name);
    }

    public int? RequiredPositiveInteger(string name)
    {
        if (Read(name) is null)
        {
            Missing(name, "a whole number greater than zero, such as 1");
            return null;
        }

        return PositiveIntegerOf(name);
    }

    public int? OptionalPositiveInteger(string name)
    {
        return Read(name) is null ? null : PositiveIntegerOf(name);
    }

    // Whether the agent gave the argument at all, whatever its value.
    public bool Has(string name)
    {
        return _arguments.ValueKind == JsonValueKind.Object && _arguments.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null;
    }

    public IReadOnlyList<Error> Errors()
    {
        var errors = new List<Error>(_errors);

        if (_arguments.ValueKind == JsonValueKind.Object)
        {
            var known = string.Join(", ", _known.Select(name => $"'{name}'"));
            errors.AddRange(_arguments.EnumerateObject()
                .Where(argument => !_known.Contains(argument.Name, StringComparer.Ordinal))
                .Select(argument => new Error(
                    ToolErrorCodes.ArgumentUnknown,
                    _known.Count == 0
                        ? $"This tool takes no arguments, and '{argument.Name}' was given."
                        : $"'{argument.Name}' is not an argument of this tool; it takes {known}.")));
        }

        return errors;
    }

    private string? String(string name, string example, bool required)
    {
        if (Read(name) is not { } value)
        {
            if (required)
            {
                Missing(name, $"a string such as '{example}'");
            }

            return null;
        }

        if (value.ValueKind == JsonValueKind.String && value.GetString() is { } text && !string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        Invalid(name, $"must be a non-empty string such as '{example}'");
        return null;
    }

    private int? PositiveIntegerOf(string name)
    {
        var value = _arguments.GetProperty(name);

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number > 0)
        {
            return number;
        }

        Invalid(name, "must be a whole number greater than zero");
        return null;
    }

    private Guid? GuidOf(string name)
    {
        var value = _arguments.GetProperty(name);
        if (value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var id))
        {
            return id;
        }

        Invalid(name, "must be a UUID string such as '0192f3a1-7c2e-7a4b-9d3e-1f2a3b4c5d6e'");
        return null;
    }

    private JsonElement? Read(string name)
    {
        _known.Add(name);

        return _arguments.ValueKind == JsonValueKind.Object
            && _arguments.TryGetProperty(name, out var value)
            && value.ValueKind != JsonValueKind.Null
                ? value
                : null;
    }


    private void Missing(string name, string form)
    {
        _errors.Add(new Error(ToolErrorCodes.ArgumentMissing, $"'{name}' is required: give {form}."));
    }

    private void Invalid(string name, string requirement)
    {
        _errors.Add(new Error(ToolErrorCodes.ArgumentInvalid, $"'{name}' {requirement}."));
    }
}
