using System.Text.Json;
using System.Text.RegularExpressions;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Media;
using FluentCMS.Core.Results;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Builtin.Checks;

// Every image in the text has alt text that describes it (CHK-01): Markdown images, inline or by reference, and HTML img elements.
// A media field that names an image passes when the version readers will see has alt text (MED-02): the one the content's change set
// proposes, else the live one, which a person's write in the library is at once. An image only another set proposes fails, since
// nothing checks what goes live with it later. Other media, such as a PDF, needs no alt text. An asset the library does not hold is
// left to the reference checks of entries.
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

    public async Task<CheckOutcome> EvaluateAsync(CheckInput input, CancellationToken cancellationToken)
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

        findings.AddRange(await MediaFindingsAsync(input, cancellationToken));

        return findings.Count == 0 ? CheckOutcome.Pass() : CheckOutcome.Fail(findings);
    }

    private static async Task<List<CheckFinding>> MediaFindingsAsync(CheckInput input, CancellationToken cancellationToken)
    {
        var findings = new List<CheckFinding>();
        if (input.Subject.Definition is not { } definition || input.Services.GetService<IMediaLibrary>() is not { } library)
        {
            return findings;
        }

        var proposals = (input.Subject.SetOperations ?? [])
            .Where(operation => operation.ItemKind == ItemKind.Media)
            .DistinctBy(operation => operation.ItemId)
            .ToDictionary(operation => operation.ItemId, operation => operation.ProposedVersion);
        foreach (var (pointer, value) in CheckContent.FieldValues(input.Subject))
        {
            var field = definition.Fields.FirstOrDefault(candidate => candidate.Name == CheckContent.FieldOf(pointer));
            if (field is null
                || !MediaFields.Holds(field)
                || value.ValueKind != JsonValueKind.String
                || !Guid.TryParse(value.GetString(), out var id)
                || await library.GetAsync(id, cancellationToken) is not { IsSuccess: true } asset
                || !asset.Value.Latest.Blob.ContentType.StartsWith("image/", StringComparison.Ordinal))
            {
                continue;
            }

            var held = proposals.TryGetValue(id, out var proposed);
            var shown = held
                ? await ProposedAsync(library, asset.Value, proposed, cancellationToken)
                : (await library.LiveVersionAsync(id, cancellationToken)) is { IsSuccess: true } live ? live.Value : null;
            if (shown is null)
            {
                // Only a change set proposes it: whichever version some other set publishes later, this content is not checked
                // against it, so the image goes live here with no alt text a reader can rely on.
                if (!held)
                {
                    findings.Add(new CheckFinding(
                        $"The image '{asset.Value.Latest.Metadata.FileName}' in {field.Name} is not published; give it alt text with update_media "
                        + "in the change set this content goes into, so both go live together.",
                        pointer,
                        id.ToString()));
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(shown.Metadata.AltText))
            {
                findings.Add(new CheckFinding(
                    $"Add alt text to the image '{shown.Metadata.FileName}' in {field.Name} that says what it shows; set it with update_media.",
                    pointer,
                    id.ToString()));
            }
        }

        return findings;
    }

    // The version the subject's change set proposes, which goes live with the content; null when the set takes the asset offline.
    private static async Task<MediaAssetVersion?> ProposedAsync(IMediaLibrary library, MediaAsset asset, int? proposed, CancellationToken cancellationToken)
    {
        if (proposed is not { } number)
        {
            return null;
        }

        return number == asset.LatestVersion
            ? asset.Latest
            : (await library.GetVersionAsync(asset.Id, number, cancellationToken)) is { IsSuccess: true } stored ? stored.Value : null;
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
