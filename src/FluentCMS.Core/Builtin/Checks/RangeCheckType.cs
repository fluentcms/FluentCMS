using System.Globalization;
using System.Text.Json;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Builtin.Checks;

// The range each number must fall in (CHK-01), such as a price from 0 to 10000. It reads each field's number, each number of a
// field that holds a list, and the value of each fact of an entity (SCH-03).
internal sealed class RangeCheckType : ICheckType
{
    public string Name => "range";

    public CheckKind Kind => CheckKind.Deterministic;

    public IReadOnlyList<Error> ValidateSettings(JsonElement settings)
    {
        var reader = new CheckSettingsReader(Name, settings);
        var min = reader.Number("min");
        var max = reader.Number("max");

        if (min is null && max is null && settings.ValueKind == JsonValueKind.Object)
        {
            reader.Invalid("max", "or 'min' must be given");
        }

        if (min > max)
        {
            reader.Invalid("min", $"must not be greater than 'max', which is {max?.ToString(CultureInfo.InvariantCulture)}");
        }

        return reader.Errors();
    }

    public Task<CheckOutcome> EvaluateAsync(CheckInput input, CancellationToken cancellationToken)
    {
        var reader = new CheckSettingsReader(Name, input.Settings);
        var min = reader.Number("min");
        var max = reader.Number("max");
        var findings = new List<CheckFinding>();

        foreach (var (pointer, value) in Numbers(input.Subject))
        {
            var field = CheckContent.FieldOf(pointer);
            var shown = value.GetRawText();

            // A number too large for decimal lies beyond any limit a check can name, on the side its sign says.
            var tooLarge = !value.TryGetDecimal(out var number);
            var negative = shown.StartsWith('-');
            if (max is not null && (tooLarge ? !negative : number > max))
            {
                findings.Add(new CheckFinding($"Change {field} to at most {Show(max)}; it is {shown}.", pointer, shown));
            }
            else if (min is not null && (tooLarge ? negative : number < min))
            {
                findings.Add(new CheckFinding($"Change {field} to at least {Show(min)}; it is {shown}.", pointer, shown));
            }
        }

        return Task.FromResult(findings.Count == 0 ? CheckOutcome.Pass() : CheckOutcome.Fail(findings));
    }

    private static IEnumerable<(string Pointer, JsonElement Value)> Numbers(CheckSubject subject)
    {
        foreach (var (pointer, value) in CheckContent.FieldValues(subject.Fields))
        {
            if (value.ValueKind == JsonValueKind.Number)
            {
                yield return (pointer, value);
            }
            else if (subject.Definition is { IsEntity: true }
                && value.ValueKind == JsonValueKind.Object
                && value.TryGetProperty(FactMetadata.Value, out var fact)
                && fact.ValueKind == JsonValueKind.Number)
            {
                yield return (CheckContent.Append(pointer, FactMetadata.Value), fact);
            }
        }
    }

    private static string? Show(decimal? number)
    {
        return number?.ToString(CultureInfo.InvariantCulture);
    }
}
