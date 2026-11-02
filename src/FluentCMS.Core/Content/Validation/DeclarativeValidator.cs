using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using FluentCMS.Content;
using FluentCMS.Schema;

namespace FluentCMS.Core.Content.Validation;

// A field that was checked, so the custom field validators see exactly the fields the declarative rules saw.
internal sealed record FieldVisit(SchemaType Owner, SchemaField Field, string Path, object? Value, IReadOnlyDictionary<string, object?> Siblings);

// Required, RequiredWhen, length, range, pattern and select options, for every visible field, recursing into
// components and blocks. A block or component instance is a dictionary keyed by API field name (a block carries
// its block name under "type") or a CLR object whose public properties are the fields.
internal sealed class DeclarativeValidator(SchemaModel model)
{
    private const string TypeKey = "type";
    private const int MaxNestingDepth = 8;
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private readonly ConcurrentDictionary<SchemaField, Regex> _patterns = new();

    public void Validate(SchemaType type, IReadOnlyDictionary<string, object?> values, ValidationContext context, List<FieldVisit> visits) =>
        ValidateFields(type, values, "", context, visits, 0);

    private void ValidateFields(SchemaType type, IReadOnlyDictionary<string, object?> values, string prefix, ValidationContext context, List<FieldVisit> visits, int depth)
    {
        foreach (var field in type.Fields)
        {
            // A hidden field is not part of the entry, whatever it holds.
            if (field.VisibleWhen is not null && !ConditionEvaluator.Evaluate(field.VisibleWhen, values))
            {
                continue;
            }

            values.TryGetValue(field.Name, out var value);
            var path = prefix + field.Name;
            visits.Add(new FieldVisit(type, field, path, value, values));

            if (FieldValue.IsEmpty(value))
            {
                CheckRequired(field, path, values, context);
                continue;
            }

            CheckLength(field, path, value, context);
            CheckItems(field, path, value, context);

            if (depth < MaxNestingDepth)
            {
                Recurse(field, path, value, context, visits, depth + 1);
            }
        }
    }

    private static void CheckRequired(SchemaField field, string path, IReadOnlyDictionary<string, object?> values, ValidationContext context)
    {
        if (context.Mode != ValidationMode.Publish)
        {
            return;
        }

        if (field.Required)
        {
            context.AddError(path, ValidationErrorCodes.Required, "A value is required.");
        }
        else if (field.RequiredWhen is not null && ConditionEvaluator.Evaluate(field.RequiredWhen, values))
        {
            context.AddError(path, ValidationErrorCodes.RequiredByCondition, $"A value is required when {field.RequiredWhen}.");
        }
    }

    // A text is limited in characters; a multiple field, and blocks, in items.
    private static void CheckLength(SchemaField field, string path, object? value, ValidationContext context)
    {
        int length;
        string unit;
        if (IsList(field) && FieldValue.Items(value) is { } items)
        {
            length = items.Count();
            unit = "items";
        }
        else if (!field.Multiple && value is string text)
        {
            length = text.Length;
            unit = "characters";
        }
        else
        {
            return;
        }

        if (field.MinLength is { } min && length < min)
        {
            context.AddError(path, ValidationErrorCodes.MinLength, $"Must have at least {min} {unit}, has {length}.");
        }

        if (field.MaxLength is { } max && length > max)
        {
            context.AddError(path, ValidationErrorCodes.MaxLength, $"Must have at most {max} {unit}, has {length}.");
        }
    }

    // The rules on the value itself, applied to each item of a multiple field at its own path (tags[1]).
    private void CheckItems(SchemaField field, string path, object? value, ValidationContext context)
    {
        if (field.FieldType is ValidatedFieldTypes.Component or ValidatedFieldTypes.Blocks)
        {
            return;
        }

        if (field.Multiple && FieldValue.Items(value) is { } items)
        {
            var index = 0;
            foreach (var item in items)
            {
                CheckValue(field, $"{path}[{index++}]", item, context);
            }
        }
        else if (!field.Multiple)
        {
            CheckValue(field, path, value, context);
        }
    }

    private void CheckValue(SchemaField field, string path, object? value, ValidationContext context)
    {
        if (FieldValue.IsEmpty(value))
        {
            return;
        }

        if (RangeValue(value) is { } number)
        {
            if (field.Minimum is { } min && number < min)
            {
                context.AddError(path, ValidationErrorCodes.BelowMinimum, $"Must be at least {Format(min)}.");
            }

            if (field.Maximum is { } max && number > max)
            {
                context.AddError(path, ValidationErrorCodes.AboveMaximum, $"Must be at most {Format(max)}.");
            }
        }

        if (field.Pattern is not null && value is string text && !Matches(field, text))
        {
            context.AddError(path, ValidationErrorCodes.PatternMismatch, $"Does not match the pattern {field.Pattern}.");
        }

        if (field.FieldType == ValidatedFieldTypes.Select
            && field.Settings.TryGetValue(FieldSettings.Options, out var setting)
            && setting is IEnumerable<string> options
            && FieldValue.Normalize(value) is { } option
            && !options.Contains(option as string, StringComparer.Ordinal))
        {
            context.AddError(path, ValidationErrorCodes.NotAnAllowedOption, $"{option} is not one of: {string.Join(", ", options)}.");
        }
    }

    private void Recurse(SchemaField field, string path, object? value, ValidationContext context, List<FieldVisit> visits, int depth)
    {
        if (field.FieldType == ValidatedFieldTypes.Component
            && field.Settings.TryGetValue(FieldSettings.Component, out var name)
            && model.FindComponent((string)name) is { } component)
        {
            if (field.Multiple)
            {
                var index = 0;
                foreach (var item in FieldValue.Items(value) ?? [])
                {
                    ValidateInstance(component, item, $"{path}[{index++}].", context, visits, depth);
                }
            }
            else
            {
                ValidateInstance(component, value, $"{path}.", context, visits, depth);
            }
        }
        else if (field.FieldType == ValidatedFieldTypes.Blocks)
        {
            var allowed = field.Settings.TryGetValue(FieldSettings.Blocks, out var setting) && setting is IEnumerable<string> names ? names.ToList() : [];
            var index = 0;
            foreach (var item in FieldValue.Items(value) ?? [])
            {
                var itemPath = $"{path}[{index++}]";
                var block = FindBlock(item);
                if (block is null || !allowed.Contains(block.Name))
                {
                    context.AddError(itemPath, ValidationErrorCodes.NotAnAllowedOption, $"The block type {BlockName(item) ?? "(none)"} is not one of: {string.Join(", ", allowed)}.");
                    continue;
                }

                ValidateInstance(block, item, $"{itemPath}.", context, visits, depth);
            }
        }
    }

    private void ValidateInstance(SchemaType type, object? instance, string prefix, ValidationContext context, List<FieldVisit> visits, int depth)
    {
        if (FieldValue.ReadInstance(instance) is { } values)
        {
            ValidateFields(type, values, prefix, context, visits, depth);
        }
    }

    private SchemaType? FindBlock(object? item) => BlockName(item) is { } name ? model.FindBlock(name) : null;

    private string? BlockName(object? item)
    {
        if (item is IReadOnlyDictionary<string, object?> dictionary)
        {
            return dictionary.TryGetValue(TypeKey, out var name) ? name as string : null;
        }

        if (item is IDictionary<string, object?> mutable)
        {
            return mutable.TryGetValue(TypeKey, out var name) ? name as string : null;
        }

        return item is null ? null : model.Blocks.FirstOrDefault(block => block.ClrType == item.GetType())?.Name;
    }

    private bool Matches(SchemaField field, string text)
    {
        var regex = _patterns.GetOrAdd(field, static f => new Regex(f.Pattern!, RegexOptions.CultureInvariant, MatchTimeout));
        try
        {
            return regex.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static bool IsList(SchemaField field) => field.Multiple || field.FieldType == ValidatedFieldTypes.Blocks;

    // Numbers as they are; a date as days and a date-time as seconds since 1970-01-01, which the minimum and maximum then use.
    private static decimal? RangeValue(object? value) => value switch
    {
        byte or sbyte or short or ushort or int or uint or long or ulong or decimal => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
        float or double => double.IsFinite(Convert.ToDouble(value, CultureInfo.InvariantCulture)) ? (decimal)Convert.ToDouble(value, CultureInfo.InvariantCulture) : null,
        DateOnly day => day.DayNumber - DateOnly.FromDateTime(DateTime.UnixEpoch).DayNumber,
        DateTimeOffset moment => moment.ToUnixTimeSeconds(),
        DateTime moment => new DateTimeOffset(moment.ToUniversalTime()).ToUnixTimeSeconds(),
        _ => null,
    };

    private static string Format(decimal number) => number.ToString("G29", CultureInfo.InvariantCulture);
}

internal static class ValidatedFieldTypes
{
    public const string Select = "select";
    public const string Component = "component";
    public const string Blocks = "blocks";
}
