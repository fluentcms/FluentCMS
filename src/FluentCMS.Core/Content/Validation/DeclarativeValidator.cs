using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentCMS.Content;
using FluentCMS.Core.ProseMirror;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Schema;

namespace FluentCMS.Core.Content.Validation;

// A field that was checked, so the custom field validators see exactly the fields the declarative rules saw.
internal sealed record FieldVisit(SchemaType Owner, SchemaField Field, string Path, object? Value, IReadOnlyDictionary<string, object?> Siblings);

// Required, RequiredWhen, length, range, pattern and select options, for every visible field, recursing into
// components, blocks and the data of inline blocks in rich text. A block or component instance is a dictionary keyed by
// API field name (a block carries its block name under "type") or a CLR object whose public properties are the fields.
internal sealed class DeclarativeValidator(SchemaModel model, ContentEntryMapper mapper)
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
            // A field its condition hides is not checked, by these rules or a custom validator, whatever it holds. A save keeps
            // its value, so that it is still there when the condition shows it again (spec 4.2).
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

    // A text is limited in characters, to the storage default of its type when it declares no maximum; a multiple field,
    // and blocks, in items.
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

        if (FieldLengths.Max(field) is { } max && length > max)
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
        else if (field.FieldType == ValidatedFieldTypes.RichText && value is RichText document)
        {
            foreach (var inline in InlineBlocks(document))
            {
                if (model.FindBlock(inline.BlockType) is { } block)
                {
                    ValidateInstance(block, mapper.InlineBlockValues(block, inline.Data), $"{path}{inline.DataPath}.", context, visits, depth);
                }
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

    // A document that cannot be read is for the rich text rules to refuse.
    private static List<InlineBlockJsonNode> InlineBlocks(RichText document)
    {
        try
        {
            return InlineBlockJson.MayHold(document.Json) ? InlineBlockJson.Find(InlineBlockJson.Parse(document.Json)) : [];
        }
        catch (JsonException)
        {
            return [];
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

    // Only numbers have a range; the model does not say whether a bound on a date means days or seconds.
    // A float beyond what a decimal holds counts as the nearest decimal bound, so it is out of range in its direction.
    private static decimal? RangeValue(object? value) => value switch
    {
        byte or sbyte or short or ushort or int or uint or long or ulong or decimal => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
        float or double => Clamp(Convert.ToDouble(value, CultureInfo.InvariantCulture)),
        _ => null,
    };

    private static decimal? Clamp(double number) => number switch
    {
        double.NaN => null,
        >= (double)decimal.MaxValue => decimal.MaxValue,
        <= (double)decimal.MinValue => decimal.MinValue,
        _ => (decimal)number,
    };

    private static string Format(decimal number) => number.ToString("G29", CultureInfo.InvariantCulture);
}

internal static class ValidatedFieldTypes
{
    public const string Select = "select";
    public const string Component = "component";
    public const string Blocks = "blocks";
    public const string RichText = "richText";
}
