using System.Globalization;
using System.Text;

namespace FluentCMS.Admin.Fields;

// The slug the server derives from a source field (SlugFieldType.Slugify of FluentCMS.Core), so that the slug shown while
// the user types is the one a save without it would get. The admin is a client of the API only, so it keeps its own copy;
// SlugsTests pins the cases.
internal static class Slugs
{
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

    private static bool IsSlugLetterOrDigit(Rune rune) =>
        !IsFiller(rune) && Rune.GetUnicodeCategory(rune) is UnicodeCategory.LowercaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or UnicodeCategory.DecimalDigitNumber;

    private static bool IsMark(Rune rune) =>
        Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    private static bool IsFiller(Rune rune) => rune.Value is 0x0640 or 0x115F or 0x1160 or 0x3164 or 0xFFA0;
}
