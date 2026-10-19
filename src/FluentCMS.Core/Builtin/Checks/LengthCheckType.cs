using System.Text.Json;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Builtin.Checks;

// How long each text may be, in characters or words (CHK-01), such as a headline of at most 60 characters. Each field's text is
// measured on its own, and each text of a field that holds a list; characters are counted as code points, as field types count them.
internal sealed class LengthCheckType : ICheckType
{
    public string Name => "length";

    public CheckKind Kind => CheckKind.Deterministic;

    public IReadOnlyList<Error> ValidateSettings(JsonElement settings)
    {
        var reader = new CheckSettingsReader(Name, settings);
        var (min, max, _) = Read(reader);

        if (min is null && max is null && settings.ValueKind == JsonValueKind.Object)
        {
            reader.Invalid("max", "or 'min' must be given");
        }

        if (min > max)
        {
            reader.Invalid("min", $"must not be greater than 'max', which is {max}");
        }

        return reader.Errors();
    }

    public Task<CheckOutcome> EvaluateAsync(CheckInput input, CancellationToken cancellationToken)
    {
        var (min, max, words) = Read(new CheckSettingsReader(Name, input.Settings));
        var unit = words ? "words" : "characters";
        var findings = new List<CheckFinding>();

        foreach (var (pointer, value) in CheckContent.FieldValues(input.Subject.Fields).Where(item => item.Value.ValueKind == JsonValueKind.String))
        {
            var text = value.GetString()!;
            var length = words ? text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length : text.EnumerateRunes().Count();
            var field = CheckContent.FieldOf(pointer);

            if (length > max)
            {
                findings.Add(new CheckFinding($"Shorten {field} to at most {max} {unit}; it has {length}.", pointer, CheckContent.Quote(text)));
            }
            else if (length < min)
            {
                findings.Add(new CheckFinding($"Lengthen {field} to at least {min} {unit}; it has {length}.", pointer, CheckContent.Quote(text)));
            }
        }

        return Task.FromResult(findings.Count == 0 ? CheckOutcome.Pass() : CheckOutcome.Fail(findings));
    }

    private static (int? Min, int? Max, bool Words) Read(CheckSettingsReader reader)
    {
        var min = reader.PositiveInteger("min");
        var max = reader.PositiveInteger("max");
        var unit = reader.OptionalString("unit");
        if (unit is not (null or "characters" or "words"))
        {
            reader.Invalid("unit", "must be \"characters\" or \"words\"");
        }

        return (min, max, unit == "words");
    }
}
