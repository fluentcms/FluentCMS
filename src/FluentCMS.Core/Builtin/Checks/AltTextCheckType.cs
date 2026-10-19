using System.Text.Json;
using System.Text.RegularExpressions;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Builtin.Checks;

// Every image in the text has alt text that describes it (CHK-01): Markdown images, inline or by reference, and HTML img elements.
// Alt text of assets in the media library is checked there (F-MED).
internal sealed class AltTextCheckType : ICheckType
{
    private const RegexOptions Linear = RegexOptions.NonBacktracking | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private static readonly Regex MarkdownImage = new(@"!\[((?:[^\[\]]|\[[^\[\]]*\])*)\](?:\([^)\r\n]*\)|\[[^\]\r\n]*\])", Linear, MatchTimeout);

    private static readonly Regex HtmlImage = new(@"<img\b[^>]*>", Linear, MatchTimeout);

    // One attribute of an element: a name, then a value in double quotes, single quotes or none.
    private static readonly Regex Attribute = new(@"([^\s=/>""']+)(?:\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s""'=<>`]+)))?", Linear, MatchTimeout);

    public string Name => "altText";

    public CheckKind Kind => CheckKind.Deterministic;

    public IReadOnlyList<Error> ValidateSettings(JsonElement settings)
    {
        return new CheckSettingsReader(Name, settings).Errors();
    }

    public Task<CheckOutcome> EvaluateAsync(CheckInput input, CancellationToken cancellationToken)
    {
        var findings = new List<CheckFinding>();

        foreach (var (pointer, text) in CheckContent.Strings(input.Subject))
        {
            var images = MarkdownImage.Matches(text)
                .Where(match => string.IsNullOrWhiteSpace(match.Groups[1].Value))
                .Concat(HtmlImage.Matches(text).Where(match => string.IsNullOrWhiteSpace(Alt(match.Value))))
                .OrderBy(match => match.Index);

            findings.AddRange(images.Select(image => new CheckFinding(
                $"Add alt text to the image '{CheckContent.Quote(image.Value, 80)}' in {CheckContent.FieldOf(pointer)} that says what it shows.",
                pointer,
                image.Value)));
        }

        return Task.FromResult(findings.Count == 0 ? CheckOutcome.Pass() : CheckOutcome.Fail(findings));
    }

    // The value of the element's alt attribute, read attribute by attribute so a quoted value or a data-alt is not taken for it.
    private static string? Alt(string element)
    {
        foreach (Match attribute in Attribute.Matches(element[4..^1]))
        {
            if (string.Equals(attribute.Groups[1].Value, "alt", StringComparison.OrdinalIgnoreCase))
            {
                return attribute.Groups.Values.Skip(2).FirstOrDefault(group => group.Success)?.Value ?? string.Empty;
            }
        }

        return null;
    }
}
