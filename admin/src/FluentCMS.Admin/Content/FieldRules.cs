using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Localization;
using Microsoft.Extensions.Localization;

namespace FluentCMS.Admin.Content;

// The rules of a field's validation the form checks itself, as the server's DeclarativeValidator does (spec 4.5): lengths in
// characters for a text and in items for a list, range for numbers and pattern for texts, each item of a multiple field at its
// own path. required is checked before a publish only, since a draft skips it; unique is the server's.
internal static class FieldRules
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);
    private static readonly ConcurrentDictionary<string, Regex?> Patterns = new(StringComparer.Ordinal);

    public static List<ApiProblemError> Check(SchemaFieldDescription field, JsonNode? value, IStringLocalizer<AdminResources> l)
    {
        ArgumentNullException.ThrowIfNull(field);
        List<ApiProblemError> errors = [];
        if (FormConditions.IsEmpty(value))
        {
            return errors;
        }

        var rules = field.Validation;
        if ((field.Multiple || field.Type == "blocks") && value is JsonArray items)
        {
            Length(field.Name, items.Count, rules, "Items", errors, l);
            if (field.Type is not ("component" or "blocks"))
            {
                for (var index = 0; index < items.Count; index++)
                {
                    Value(field.Name + "[" + index.ToString(CultureInfo.InvariantCulture) + "]", items[index], rules, errors, l);
                }
            }
        }
        else if (!field.Multiple)
        {
            if (value is JsonValue text && text.GetValueKind() == JsonValueKind.String)
            {
                Length(field.Name, text.GetValue<string>().Length, rules, "Characters", errors, l);
            }

            Value(field.Name, value, rules, errors, l);
        }

        return errors;
    }

    public static ApiProblemError Required(SchemaFieldDescription field, IStringLocalizer<AdminResources> l) =>
        new() { Path = field.Name, Code = "required", Message = l["Content.Form.Rule.Required"] };

    private static void Length(string path, int length, FieldValidationDescription rules, string unit, List<ApiProblemError> errors, IStringLocalizer<AdminResources> l)
    {
        if (rules.MinLength is { } min && length < min)
        {
            errors.Add(new() { Path = path, Code = "minLength", Message = l[$"Content.Form.Rule.MinLength{unit}", min, length] });
        }

        if (rules.MaxLength is { } max && length > max)
        {
            errors.Add(new() { Path = path, Code = "maxLength", Message = l[$"Content.Form.Rule.MaxLength{unit}", max, length] });
        }
    }

    private static void Value(string path, JsonNode? value, FieldValidationDescription rules, List<ApiProblemError> errors, IStringLocalizer<AdminResources> l)
    {
        if (FormConditions.IsEmpty(value) || value is not JsonValue item)
        {
            return;
        }

        if (item.GetValueKind() == JsonValueKind.Number && Number(item) is { } number)
        {
            if (rules.Minimum is { } min && number < min)
            {
                errors.Add(new() { Path = path, Code = "belowMinimum", Message = l["Content.Form.Rule.Minimum", Format(min)] });
            }

            if (rules.Maximum is { } max && number > max)
            {
                errors.Add(new() { Path = path, Code = "aboveMaximum", Message = l["Content.Form.Rule.Maximum", Format(max)] });
            }
        }

        if (rules.Pattern is { } pattern && item.GetValueKind() == JsonValueKind.String && !Matches(pattern, item.GetValue<string>()))
        {
            errors.Add(new() { Path = path, Code = "patternMismatch", Message = l["Content.Form.Rule.Pattern"] });
        }
    }

    // A pattern this runtime cannot read is left to the server; a match that runs too long fails, as on the server.
    private static bool Matches(string pattern, string text)
    {
        if (Patterns.GetOrAdd(pattern, Compile) is not { } regex)
        {
            return true;
        }

        try
        {
            return regex.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static Regex? Compile(string pattern)
    {
        try
        {
            return new Regex(pattern, RegexOptions.CultureInvariant, MatchTimeout);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    // A number beyond what a decimal holds counts as the nearest bound, so it is out of range in its direction.
    private static decimal? Number(JsonValue value)
    {
        if (value.TryGetValue<decimal>(out var number))
        {
            return number;
        }

        return double.TryParse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var big)
            ? big >= (double)decimal.MaxValue ? decimal.MaxValue : big <= (double)decimal.MinValue ? decimal.MinValue : (decimal)big
            : null;
    }

    private static string Format(decimal number) => number.ToString("G29", CultureInfo.CurrentCulture);
}
