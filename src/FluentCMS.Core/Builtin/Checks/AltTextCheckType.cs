using System.Text.Json;
using System.Text.RegularExpressions;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Builtin.Checks;

// Every image in the text has alt text that describes it (CHK-01): Markdown images and HTML img elements. Alt text of assets in
// the media library is checked there (F-MED).
internal sealed partial class AltTextCheckType : ICheckType
{
    public string Name => "altText";

    public CheckKind Kind => CheckKind.Deterministic;

    public IReadOnlyList<Error> ValidateSettings(JsonElement settings)
    {
        return new CheckSettingsReader(Name, settings).Errors();
    }

    public Task<CheckOutcome> EvaluateAsync(CheckInput input, CancellationToken cancellationToken)
    {
        var findings = new List<CheckFinding>();

        foreach (var (pointer, text) in CheckContent.Strings(input.Subject.Fields))
        {
            var images = MarkdownImage().Matches(text)
                .Where(match => string.IsNullOrWhiteSpace(match.Groups[1].Value))
                .Concat(HtmlImage().Matches(text).Where(match => !HasAlt(match.Value)))
                .OrderBy(match => match.Index);

            findings.AddRange(images.Select(image => new CheckFinding(
                $"Add alt text to the image '{CheckContent.Quote(image.Value, 80)}' in {CheckContent.FieldOf(pointer)} that says what it shows.",
                pointer,
                image.Value)));
        }

        return Task.FromResult(findings.Count == 0 ? CheckOutcome.Pass() : CheckOutcome.Fail(findings));
    }

    private static bool HasAlt(string element)
    {
        return AltAttribute().Match(element) is { Success: true } match
            && !string.IsNullOrWhiteSpace(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);
    }

    [GeneratedRegex(@"!\[((?:[^\[\]]|\[[^\[\]]*\])*)\]\([^)]*\)", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex MarkdownImage();

    [GeneratedRegex(@"<img\b[^>]*>", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex HtmlImage();

    [GeneratedRegex(@"\balt\s*=\s*(?:""([^""]*)""|'([^']*)')", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex AltAttribute();
}
