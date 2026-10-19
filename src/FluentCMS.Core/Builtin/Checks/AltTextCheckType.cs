using System.Text.Json;
using System.Text.RegularExpressions;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Media;
using FluentCMS.Core.Results;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Builtin.Checks;

// Every image in the text has alt text that describes it (CHK-01): Markdown images, inline or by reference, and HTML img elements.
// A media field that names an image passes when the version readers will see has alt text; other media, such as a PDF, needs
// none. An asset the library does not hold is left to the reference checks of entries.
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
                || await ShownAsync(library, id, proposals, cancellationToken) is not { } shown
                || !shown.Blob.ContentType.StartsWith("image/", StringComparison.Ordinal)
                || !string.IsNullOrWhiteSpace(shown.Metadata.AltText))
            {
                continue;
            }

            findings.Add(new CheckFinding(
                $"Add alt text to the image '{shown.Metadata.FileName}' in {field.Name} that says what it shows; set it with update_media.",
                pointer,
                id.ToString()));
        }

        return findings;
    }

    // The version of an asset that readers will see once the content goes live (MED-02): the one the subject's change set proposes
    // when it holds the asset, else the published one. An asset never published and outside the set is read at its latest version,
    // the best guess at what will go live with it. Null when the asset is unknown or the set unpublishes it.
    private static async Task<MediaAssetVersion?> ShownAsync(
        IMediaLibrary library,
        Guid id,
        IReadOnlyDictionary<Guid, int?> proposals,
        CancellationToken cancellationToken)
    {
        if (await library.GetAsync(id, cancellationToken) is not { IsSuccess: true } asset)
        {
            return null;
        }

        var version = proposals.TryGetValue(id, out var proposed) ? proposed : asset.Value.PublishedVersion ?? asset.Value.LatestVersion;
        if (version is not { } number)
        {
            return null;
        }

        return number == asset.Value.LatestVersion
            ? asset.Value.Latest
            : (await library.GetVersionAsync(id, number, cancellationToken)) is { IsSuccess: true } stored ? stored.Value : null;
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
