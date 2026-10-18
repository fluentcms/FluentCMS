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

    public Guid? RequiredGuid(string name)
    {
        if (Read(name) is not { } value)
        {
            Missing(name, "a UUID such as '0192f3a1-7c2e-7a4b-9d3e-1f2a3b4c5d6e'");
            return null;
        }

        if (value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var id))
        {
            return id;
        }

        Invalid(name, "must be a UUID string such as '0192f3a1-7c2e-7a4b-9d3e-1f2a3b4c5d6e'");
        return null;
    }

    public int? OptionalPositiveInteger(string name)
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
