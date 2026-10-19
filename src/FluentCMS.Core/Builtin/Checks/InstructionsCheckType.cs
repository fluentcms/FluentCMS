using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Builtin.Checks;

// Text inside content that reads as instructions to a model, which every model call treats as data (SEC-03): phrases that tell a
// model to drop its instructions or take a new role, chat-template markers, and invisible characters that can hide such text from
// a reader. 'phrases' adds the site's own. A content writer rarely needs any of them, so a finding asks to rewrite or remove it.
internal sealed class InstructionsCheckType : ICheckType
{
    private const RegexOptions Linear = RegexOptions.NonBacktracking | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    // Each alternative needs words ordinary copy does not use together, so "New instructions: unplug the kettle", "System: Windows 11"
    // or "ignore the instructions on the box" in a product page is not flagged.
    private static readonly Regex Instruction = new(
        @"\b(?:ignore|disregard|forget|override|bypass)\s+(?:(?:all|any|and|of|the|your|my|these|those)\s+)*"
        + @"(?:previous|prior|above|earlier|preceding|original|former|system)\s+(?:instructions?|prompts?|rules|directions|guidelines|messages|context)\b"
        + @"|\b(?:ignore|disregard|forget)\s+(?:all|any|your)\s+(?:other\s+)?(?:instructions|prompts)\b"
        + @"|\b(?:ignore|disregard)\s+(?:everything|all)\s+(?:above|before)\b"
        + @"|\bignore\s+the\s+above\b"
        + @"|\b(?:you\s+are\s+now|act\s+as|pretend\s+to\s+be)\s+(?:a|an)\s+(?:ai|chat\s*bot|language\s+model|llm|(?:ai|virtual)\s+assistant)\b"
        + @"|\bsystem\s+(?:prompt|instructions?)\s*:"
        + @"|\b(?:reveal|repeat|print|show|output)\s+(?:your|the)\s+(?:system|hidden)\s+prompt\b"
        + @"|\b(?:reveal|repeat)\s+your\s+instructions\b"
        + @"|<\|(?:im_start|im_end|system|user|assistant|endoftext)\|>"
        + @"|\[/?INST\]|<</?SYS>>"
        + @"|^[ \t]*###[ \t]*(?:Instruction|Response|System)[ \t]*:[ \t]*$",
        Linear | RegexOptions.Multiline,
        MatchTimeout);

    // Letters of other scripts drawn like Latin ones, after Unicode's confusables (UTS #39), mapped to the Latin letter they imitate.
    private static readonly FrozenDictionary<char, char> LatinLookalikes = new Dictionary<int, char>
    {
        // Cyrillic
        [0x0430] = 'a', [0x0435] = 'e', [0x0456] = 'i', [0x0458] = 'j', [0x043E] = 'o', [0x0440] = 'p', [0x0441] = 'c', [0x0443] = 'y',
        [0x0445] = 'x', [0x0455] = 's', [0x0501] = 'd', [0x051B] = 'q', [0x051D] = 'w', [0x04BB] = 'h', [0x04CF] = 'l', [0x0475] = 'v',
        [0x0410] = 'A', [0x0412] = 'B', [0x0415] = 'E', [0x041A] = 'K', [0x041C] = 'M', [0x041D] = 'H', [0x041E] = 'O', [0x0420] = 'P',
        [0x0421] = 'C', [0x0422] = 'T', [0x0425] = 'X', [0x0423] = 'Y', [0x0406] = 'I', [0x0408] = 'J', [0x0405] = 'S', [0x04AE] = 'Y',
        [0x050C] = 'G', [0x051A] = 'Q', [0x051C] = 'W',

        // Greek
        [0x03B1] = 'a', [0x03BF] = 'o', [0x03BD] = 'v', [0x03C1] = 'p', [0x03B9] = 'i', [0x03BA] = 'k', [0x03C5] = 'u', [0x03F3] = 'j',
        [0x0391] = 'A', [0x0392] = 'B', [0x0395] = 'E', [0x0396] = 'Z', [0x0397] = 'H', [0x0399] = 'I', [0x039A] = 'K', [0x039C] = 'M',
        [0x039D] = 'N', [0x039F] = 'O', [0x03A1] = 'P', [0x03A4] = 'T', [0x03A5] = 'Y', [0x03A7] = 'X',

        // Armenian, and Latin letters outside ASCII
        [0x0585] = 'o', [0x057D] = 'u', [0x0570] = 'h', [0x0578] = 'n',
        [0x0131] = 'i', [0x0251] = 'a', [0x0261] = 'g', [0x0269] = 'i', [0x026A] = 'i', [0x0237] = 'j',
    }.ToFrozenDictionary(pair => (char)pair.Key, pair => pair.Value);

    public string Name => "instructions";

    public CheckKind Kind => CheckKind.Deterministic;

    public IReadOnlyList<Error> ValidateSettings(JsonElement settings)
    {
        var reader = new CheckSettingsReader(Name, settings);
        reader.OptionalStringList("phrases");
        return reader.Errors();
    }

    public Task<CheckOutcome> EvaluateAsync(CheckInput input, CancellationToken cancellationToken)
    {
        var phrases = (new CheckSettingsReader(Name, input.Settings).OptionalStringList("phrases") ?? [])
            .Select(phrase => Fold(phrase).Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape))
            .Select(words => new Regex(string.Join(@"\s+", words), Linear, MatchTimeout))
            .ToList();
        var findings = new List<CheckFinding>();

        // A model reads a property name with the content it names, so names are read as well as text.
        foreach (var (pointer, text) in CheckContent.Strings(input.Subject).Concat(CheckContent.Names(input.Subject)))
        {
            var field = CheckContent.FieldOf(pointer);
            var folded = Fold(text);
            foreach (var match in phrases.SelectMany(phrase => phrase.Matches(folded.Text)).Concat(Instruction.Matches(folded.Text)).OrderBy(match => match.Index))
            {
                var written = text[folded.Starts[match.Index]..folded.Ends[match.Index + match.Length - 1]];
                findings.Add(new CheckFinding(
                    $"Rewrite or remove '{CheckContent.Quote(written, 80)}' in {field}; it reads as an instruction to an AI model, not as content.",
                    pointer,
                    written));
            }

            foreach (var hidden in Hidden(text))
            {
                findings.Add(new CheckFinding(
                    $"Remove the invisible character {hidden} from {field}; it can hide text from the people who review it.",
                    pointer,
                    hidden));
            }
        }

        return Task.FromResult(findings.Count == 0 ? CheckOutcome.Pass() : CheckOutcome.Fail(findings));
    }

    // The text as a model may read it, so a look-alike spelling hides nothing: compatibility forms such as fullwidth or mathematical
    // letters decomposed (NFKD), accents and other combining marks dropped, letters of other scripts that look Latin mapped to the
    // letter they imitate, and characters that render as nothing, such as a soft hyphen or zero-width space, left out. Each folded
    // character keeps where it came from, so a finding quotes the text as it was written.
    private static (string Text, int[] Starts, int[] Ends) Fold(string text)
    {
        var builder = new StringBuilder(text.Length);
        var starts = new List<int>(text.Length);
        var ends = new List<int>(text.Length);
        var index = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var width = rune.Utf16SequenceLength;
            if (Rune.GetUnicodeCategory(rune) != UnicodeCategory.Format && !IsVariationSelector(rune))
            {
                foreach (var character in rune.ToString().Normalize(NormalizationForm.FormKD))
                {
                    if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                    {
                        continue;
                    }

                    builder.Append(LatinLookalikes.GetValueOrDefault(character, character));
                    starts.Add(index);
                    ends.Add(index + width);
                }
            }

            index += width;
        }

        return (builder.ToString(), [.. starts], [.. ends]);
    }

    // Zero-width and direction-changing characters, tag characters, and runs of variation selectors, which can each spell text no
    // one sees. A zero-width joiner or non-joiner between letters, as Persian and Indic scripts use them, or inside an emoji sequence
    // is part of the text.
    private static IEnumerable<string> Hidden(string text)
    {
        var runes = text.EnumerateRunes().ToList();
        var found = new List<string>();

        for (var index = 0; index < runes.Count; index++)
        {
            var value = runes[index].Value;
            var hidden = value switch
            {
                0x200B or 0x2060 or 0xFEFF or (>= 0x202A and <= 0x202E) or (>= 0x2066 and <= 0x2069) or (>= 0xE0000 and <= 0xE007F) => true,
                0x200C or 0x200D => !Joins(runes, index),
                >= 0xE0100 and <= 0xE01EF => index + 1 < runes.Count && runes[index + 1].Value is >= 0xE0100 and <= 0xE01EF,
                _ => false,
            };

            if (hidden)
            {
                found.Add("U+" + value.ToString("X4", CultureInfo.InvariantCulture));
            }
        }

        return found.Distinct();
    }

    private static bool Joins(List<Rune> runes, int index)
    {
        if (index == 0 || index == runes.Count - 1)
        {
            return false;
        }

        var before = Rune.GetUnicodeCategory(runes[index - 1]);
        var after = Rune.GetUnicodeCategory(runes[index + 1]);

        return (IsLetterOrMark(before) && IsLetterOrMark(after))
            || (runes[index].Value == 0x200D && IsEmojiPart(runes[index - 1], before) && IsEmojiPart(runes[index + 1], after));
    }

    private static bool IsLetterOrMark(UnicodeCategory category)
    {
        return category is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
    }

    private static bool IsEmojiPart(Rune rune, UnicodeCategory category)
    {
        return category is UnicodeCategory.OtherSymbol || rune.Value == 0xFE0F || rune.Value is >= 0x1F3FB and <= 0x1F3FF;
    }

    private static bool IsVariationSelector(Rune rune)
    {
        return rune.Value is (>= 0xFE00 and <= 0xFE0F) or (>= 0xE0100 and <= 0xE01EF);
    }
}
