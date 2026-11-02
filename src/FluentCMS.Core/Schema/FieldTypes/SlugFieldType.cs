using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentCMS.Schema;

namespace FluentCMS.Core.Schema.FieldTypes;

// Uniqueness of a slug is not checked here: the partial unique index of the storage model enforces it for published
// entries and the validation pipeline for drafts.
internal sealed partial class SlugFieldType()
    : StringFieldType(FieldTypeNames.Slug, [typeof(string)], FilterSets.Text, sortable: true, indexable: true, new FieldSettingDescriptor(FieldSettings.Source, FieldSettingKind.String))
{
    public const string Pattern = "^[a-z0-9]+(?:-[a-z0-9]+)*$";

    // Letters that do not decompose into a base letter and a combining mark.
    private static readonly Dictionary<char, string> Transliterations = new()
    {
        ['ß'] = "ss",
        ['æ'] = "ae",
        ['œ'] = "oe",
        ['ø'] = "o",
        ['đ'] = "d",
        ['ð'] = "d",
        ['ł'] = "l",
        ['ı'] = "i",
        ['þ'] = "th",
    };

    public static string Slugify(string source, int? maxLength = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        StringBuilder slug = new(source.Length);
        foreach (var character in source.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var lower = char.ToLowerInvariant(character);
            if (char.IsAsciiLetterOrDigit(lower))
            {
                slug.Append(lower);
            }
            else if (Transliterations.TryGetValue(lower, out var replacement))
            {
                slug.Append(replacement);
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        var text = slug.ToString().TrimEnd('-');
        if (maxLength is { } limit && text.Length > limit)
        {
            text = text[..Math.Max(limit, 0)].TrimEnd('-');
        }

        return text;
    }

    protected override string? Check(string text) =>
        SlugPattern().IsMatch(text) ? null : "is not a slug: lower case letters and digits separated by single hyphens";

    protected override void Describe(JsonObject schema) => schema["pattern"] = Pattern;

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}
