using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Builtin.Checks;

// Text inside content that reads as instructions to a model, which every model call treats as data (SEC-03): phrases that tell a
// model to drop its instructions or take a new role, chat-template markers, and invisible characters that can hide such text from
// a reader. 'phrases' adds the site's own. A content writer rarely needs any of them, so a finding asks to rewrite or remove it.
internal sealed partial class InstructionsCheckType : ICheckType
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

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
            .Select(phrase => new Regex(Regex.Escape(phrase.Trim()).Replace(@"\ ", @"\s+", StringComparison.Ordinal), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout))
            .ToList();
        var findings = new List<CheckFinding>();

        foreach (var (pointer, text) in CheckContent.Strings(input.Subject.Fields))
        {
            var field = CheckContent.FieldOf(pointer);

            foreach (var match in phrases.SelectMany(phrase => phrase.Matches(text)).Concat(Instruction().Matches(text)).OrderBy(match => match.Index))
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

    // Zero-width and direction-changing characters, and the tag characters that spell text no one sees.
    private static IEnumerable<string> Hidden(string text)
    {
        return text.EnumerateRunes()
            .Where(rune => rune.Value is 0x200B or 0x200C or 0x200D or 0x2060 or 0xFEFF or (>= 0x202A and <= 0x202E) or (>= 0x2066 and <= 0x2069)
                or (>= 0xE0000 and <= 0xE007F))
            .Select(rune => "U+" + rune.Value.ToString("X4", CultureInfo.InvariantCulture))
            .Distinct();
    }

    // Each alternative needs words ordinary copy does not use together, so "New instructions: unplug the kettle" or "System: Windows 11"
    // in a product page is not flagged.
    [GeneratedRegex(
        @"\b(?:ignore|disregard|forget|override|bypass)\s+(?:all\s+|any\s+|the\s+|your\s+|these\s+|those\s+)*"
        + @"(?:(?:previous|prior|above|earlier|preceding|original|system)\s+(?:instructions?|prompts?|rules|directions|guidelines|messages|context)|instructions|prompts?)\b"
        + @"|\b(?:you\s+are\s+(?:now\s+)?|act\s+as\s+|pretend\s+to\s+be\s+)(?:a|an)\s+(?:ai|assistant|chat\s*bot|language\s+model|llm)\b"
        + @"|\b(?:new\s+|updated\s+)?system\s+(?:instructions?|message|prompt)\s*:"
        + @"|\b(?:reveal|print|show|repeat|output)\s+(?:your|the)\s+(?:system\s+prompt|instructions|hidden\s+prompt)\b"
        + @"|\bdo\s+not\s+(?:tell|inform|show)\s+(?:the\s+)?(?:user|reviewer|human|editor)s?\b"
        + @"|<\|(?:im_start|im_end|system|user|assistant|endoftext)\|>"
        + @"|\[/?INST\]|<</?SYS>>"
        + @"|^\s*###\s*(?:Instruction|Response|System)\s*:\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant,
        1000)]
    private static partial Regex Instruction();
}
