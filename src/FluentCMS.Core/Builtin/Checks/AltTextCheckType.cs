using System.Text.Json;
using System.Text.RegularExpressions;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Media;
using FluentCMS.Core.Results;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Builtin.Checks;

// Every image in the text has alt text that describes it (CHK-01): Markdown images, inline or by reference, and HTML img elements.
// A media field that names an image passes when the version readers will see has alt text: the one the content's change set proposes,
// else the published one. An image that is neither published nor in the set fails, since nothing checks what goes live with it later.
// Other media, such as a PDF, needs no alt text. An asset the library does not hold is left to the reference checks of entries.
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

        var proposals = await ProposalsAsync(input, cancellationToken);
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

            if (await ShownAsync(library, asset.Value, proposals, cancellationToken) is not { } shown)
            {
                // Neither published nor in this set: whichever version some other set publishes later, this content is not checked
                // against it, so the image goes live here with no alt text a reader can rely on.
                if (!proposals.ContainsKey(id))
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

    // The version of an asset that readers will see once the content goes live (MED-02): the one the subject's change set proposes
    // when it holds the asset, else the published one. Null when the set unpublishes it, or when it is not published and the set does
    // not hold it.
    private static async Task<MediaAssetVersion?> ShownAsync(
        IMediaLibrary library,
        MediaAsset asset,
        IReadOnlyDictionary<Guid, int?> proposals,
        CancellationToken cancellationToken)
    {
        var version = proposals.TryGetValue(asset.Id, out var proposed) ? proposed : asset.PublishedVersion;
        if (version is not { } number)
        {
            return null;
        }

        return number == asset.LatestVersion
            ? asset.Latest
            : (await library.GetVersionAsync(asset.Id, number, cancellationToken)) is { IsSuccess: true } stored ? stored.Value : null;
    }

    // The media versions the subject's change set proposes; none when the subject belongs to no set.
    private static async Task<IReadOnlyDictionary<Guid, int?>> ProposalsAsync(CheckInput input, CancellationToken cancellationToken)
    {
        if (input.Subject.ChangeSetId is not { } changeSetId
            || input.Services.GetService<IChangeSetDirectory>() is not { } changeSets
            || await changeSets.GetAsync(changeSetId, cancellationToken) is not { IsSuccess: true } set)
        {
            return new Dictionary<Guid, int?>();
        }

        return set.Value.Operations
            .Where(operation => operation.ItemKind == ItemKind.Media)
            .ToDictionary(operation => operation.ItemId, operation => operation.ProposedVersion);
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
