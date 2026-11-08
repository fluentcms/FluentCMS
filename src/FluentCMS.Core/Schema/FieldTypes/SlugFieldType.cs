using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using FluentCMS.Schema;

namespace FluentCMS.Core.Schema.FieldTypes;

// Uniqueness of a slug is not checked here: the partial unique index of the storage model enforces it for published
// entries and the validation pipeline for drafts.
internal sealed class SlugFieldType()
    : StringFieldType(FieldTypeNames.Slug, [typeof(string)], FilterSets.Text, sortable: true, indexable: true, new FieldSettingDescriptor { Key = FieldSettings.Source, Kind = FieldSettingKind.String })
{
    public const string Pattern = @"^[\p{Ll}\p{Lm}\p{Lo}\p{Nd}]+(?:-[\p{Ll}\p{Lm}\p{Lo}\p{Nd}]+)*$";

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
        Span<char> utf16 = stackalloc char[2];
        foreach (var rune in source.Normalize(NormalizationForm.FormD).EnumerateRunes())
        {
            if (IsMark(rune) || IsFiller(rune))
            {
                continue;
            }

            var lower = Rune.ToLowerInvariant(rune);
            if (lower.IsBmp && Transliterations.TryGetValue((char)lower.Value, out var replacement))
            {
                slug.Append(replacement);
            }
            else if (IsSlugLetterOrDigit(lower))
            {
                slug.Append(utf16[..lower.EncodeToUtf16(utf16)]);
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        // Composed again, so that a Hangul syllable is one character, as it is typed.
        var text = slug.ToString().TrimEnd('-').Normalize(NormalizationForm.FormC);
        if (maxLength is { } limit && text.Length > limit)
        {
            var end = Math.Max(limit, 0);
            if (end > 0 && char.IsHighSurrogate(text[end - 1]))
            {
                end--;
            }

            text = text[..end].TrimEnd('-');
        }

        return text;
    }

    protected override string? Check(string text) =>
        IsSlug(text) ? null : "is not a slug: lower case letters and digits separated by single hyphens";

    protected override void Describe(JsonObject schema) => schema["pattern"] = Pattern;

    // What Pattern says, read by code point, since a letter outside the BMP is two chars to a .NET regular expression.
    private static bool IsSlug(string text)
    {
        var afterHyphen = true;
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value == '-')
            {
                if (afterHyphen)
                {
                    return false;
                }

                afterHyphen = true;
            }
            else if (IsSlugLetterOrDigit(rune))
            {
                afterHyphen = false;
            }
            else
            {
                return false;
            }
        }

        return !afterHyphen && text.IsNormalized(NormalizationForm.FormC);
    }

    // A letter that is still upper case after lowering has no lower case form, and is left out like any other symbol.
    private static bool IsSlugLetterOrDigit(Rune rune) =>
        !IsFiller(rune) && Rune.GetUnicodeCategory(rune) is UnicodeCategory.LowercaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or UnicodeCategory.DecimalDigitNumber;

    private static bool IsMark(Rune rune) =>
        Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    // Letters that only pad or stretch text: the Hangul fillers show nothing, and the Arabic tatweel lengthens a stroke.
    // Kept, they would make slugs that look the same and are not.
    private static bool IsFiller(Rune rune) => rune.Value is 0x0640 or 0x115F or 0x1160 or 0x3164 or 0xFFA0;
}
