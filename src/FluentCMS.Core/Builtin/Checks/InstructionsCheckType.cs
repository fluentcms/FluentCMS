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
            .Select(phrase => phrase.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape))
            .Select(words => new Regex(string.Join(@"\s+", words), Linear, MatchTimeout))
            .ToList();
        var findings = new List<CheckFinding>();

        foreach (var (pointer, text) in CheckContent.Strings(input.Subject))
        {
            var field = CheckContent.FieldOf(pointer);

            // Read without the characters that render as nothing, so a soft hyphen or zero-width space inside a word hides nothing.
            var visible = Visible(text);
            foreach (var match in phrases.SelectMany(phrase => phrase.Matches(visible)).Concat(Instruction.Matches(visible)).OrderBy(match => match.Index))
            {
                findings.Add(new CheckFinding(
                    $"Rewrite or remove '{CheckContent.Quote(match.Value, 80)}' in {field}; it reads as an instruction to an AI model, not as content.",
                    pointer,
                    match.Value));
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

    private static string Visible(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) != UnicodeCategory.Format && !IsVariationSelector(rune))
            {
                builder.Append(rune.ToString());
            }
        }

        return builder.ToString();
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
