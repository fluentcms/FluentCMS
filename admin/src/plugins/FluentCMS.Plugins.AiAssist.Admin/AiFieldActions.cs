using System.Text.Json.Nodes;
using FluentCMS.Admin;
using FluentCMS.Admin.Actions;
using FluentCMS.Admin.Plugins;
using Microsoft.Extensions.Localization;

namespace FluentCMS.Plugins.AiAssist.Admin;

// The names the server gives the actions of POST /ai/actions/{action}, and what the half's declarations repeat of the server
// half, which it never references (spec 5.1).
internal static class AiActionNames
{
    public const string Rewrite = "rewrite";
    public const string Shorten = "shorten";
    public const string Expand = "expand";
    public const string FixGrammar = "fixGrammar";
    public const string Title = "title";
    public const string Summary = "summary";
    public const string Seo = "seo";
    public const string Translate = "translate";
    public const string AltText = "altText";

    public static readonly IReadOnlyList<string> All = [Rewrite, Shorten, Expand, FixGrammar, Title, Summary, Seo, Translate, AltText];

    public const string Use = "ai.use";
    public const string ReadUsage = "ai.usage.read";

    // GET /ai/fields: the opted-in fields the caller may run actions on in the form's locale, by content type, which is empty
    // while the site names no provider; and whether the caller may ask alt text for an asset alone.
    public const string OptedInFields = "ai/fields#fields";
    public const string Assets = "ai/fields#assets";

    // The field types of the server's opt-in that the form can take a suggestion into: a rich text field takes a document,
    // and the server answers Markdown, which the admin does not turn into one.
    public const string Text = "text";
    public const string LongText = "longText";
    public const string Media = "media";
}

// An action of the half on a field of the form (spec 5.5): it shows the panel of AiSuggestions and puts the suggestion the
// user accepted into the draft, as an unsaved change; anything else leaves the draft as it was. It saves nothing.
public abstract class AiFieldAction : FieldActionBase
{
    private readonly IAdminOverlays _overlays;
    private readonly IStringLocalizer<AiAssistResources> _texts;

    private protected AiFieldAction(IAdminOverlays overlays, IStringLocalizer<AiAssistResources> texts)
    {
        _overlays = overlays;
        _texts = texts;
    }

    public override string Label => _texts[$"Ai.Action.{LabelKey}"];

    // The name of the action on the server.
    private protected abstract string Name { get; }

    private protected virtual string LabelKey => Name;

    private protected virtual bool ForAsset => false;

    public override ValueTask<bool> AppliesToAsync(FieldActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ValueTask.FromResult(Applies(context));
    }

    public override async Task<FieldActionResult> RunAsync(FieldActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var accepted = await new AiSuggestions(_overlays).AskAsync(Request(context), cancellationToken);
        return accepted is null || ForAsset ? FieldActionResult.Unchanged : FieldActionResult.Replace(JsonValue.Create(accepted));
    }

    // A text action works on the field's text, so a field without one has nothing to offer it.
    private protected virtual bool Applies(FieldActionContext context) => AiFieldValues.Text(context.Value) is not null;

    private protected virtual AiSuggestionRequest Request(FieldActionContext context) => new()
    {
        Action = Name,
        Label = Label,
        Type = context.Type.Name,
        Field = context.Field.Name,
        Locale = context.ContentLocale,
        Text = AiFieldValues.Text(context.Value),
    };
}

[FieldAction(AiActionNames.Rewrite, FieldTypes = [AiActionNames.Text, AiActionNames.LongText], Permission = AiActionNames.Use, FieldsFrom = AiActionNames.OptedInFields)]
public sealed class RewriteAction(IAdminOverlays overlays, IStringLocalizer<AiAssistResources> texts) : AiFieldAction(overlays, texts)
{
    private protected override string Name => AiActionNames.Rewrite;
}

[FieldAction(AiActionNames.Shorten, FieldTypes = [AiActionNames.Text, AiActionNames.LongText], Permission = AiActionNames.Use, FieldsFrom = AiActionNames.OptedInFields)]
public sealed class ShortenAction(IAdminOverlays overlays, IStringLocalizer<AiAssistResources> texts) : AiFieldAction(overlays, texts)
{
    private protected override string Name => AiActionNames.Shorten;
}

[FieldAction(AiActionNames.Expand, FieldTypes = [AiActionNames.Text, AiActionNames.LongText], Permission = AiActionNames.Use, FieldsFrom = AiActionNames.OptedInFields)]
public sealed class ExpandAction(IAdminOverlays overlays, IStringLocalizer<AiAssistResources> texts) : AiFieldAction(overlays, texts)
{
    private protected override string Name => AiActionNames.Expand;
}

[FieldAction(AiActionNames.FixGrammar, FieldTypes = [AiActionNames.Text, AiActionNames.LongText], Permission = AiActionNames.Use, FieldsFrom = AiActionNames.OptedInFields)]
public sealed class FixGrammarAction(IAdminOverlays overlays, IStringLocalizer<AiAssistResources> texts) : AiFieldAction(overlays, texts)
{
    private protected override string Name => AiActionNames.FixGrammar;
}

[FieldAction(AiActionNames.Title, FieldTypes = [AiActionNames.Text, AiActionNames.LongText], Permission = AiActionNames.Use, FieldsFrom = AiActionNames.OptedInFields)]
public sealed class TitleAction(IAdminOverlays overlays, IStringLocalizer<AiAssistResources> texts) : AiFieldAction(overlays, texts)
{
    private protected override string Name => AiActionNames.Title;
}

[FieldAction(AiActionNames.Summary, FieldTypes = [AiActionNames.Text, AiActionNames.LongText], Permission = AiActionNames.Use, FieldsFrom = AiActionNames.OptedInFields)]
public sealed class SummaryAction(IAdminOverlays overlays, IStringLocalizer<AiAssistResources> texts) : AiFieldAction(overlays, texts)
{
    private protected override string Name => AiActionNames.Summary;
}

// The panel offers the meta title and the meta description, either of which goes into the field.
[FieldAction(AiActionNames.Seo, FieldTypes = [AiActionNames.Text, AiActionNames.LongText], Permission = AiActionNames.Use, FieldsFrom = AiActionNames.OptedInFields)]
public sealed class SeoAction(IAdminOverlays overlays, IStringLocalizer<AiAssistResources> texts) : AiFieldAction(overlays, texts)
{
    private protected override string Name => AiActionNames.Seo;
}

// Into another enabled locale of the site, which the panel asks for; the suggestion goes into this field, as any other.
[FieldAction(AiActionNames.Translate, FieldTypes = [AiActionNames.Text, AiActionNames.LongText], Permission = AiActionNames.Use, FieldsFrom = AiActionNames.OptedInFields)]
public sealed class TranslateAction(IAdminOverlays overlays, IStringLocalizer<AiAssistResources> texts, IAdminContext admin) : AiFieldAction(overlays, texts)
{
    private protected override string Name => AiActionNames.Translate;

    private protected override bool Applies(FieldActionContext context) => base.Applies(context) && Targets(context).Count > 0;

    private protected override AiSuggestionRequest Request(FieldActionContext context) => base.Request(context) with { TargetLocales = Targets(context) };

    private List<string> Targets(FieldActionContext context)
    {
        var current = context.ContentLocale ?? admin.Site?.DefaultLocale;
        return [.. (admin.Site?.Locales ?? []).Where(locale => locale.Enabled && !string.Equals(locale.Code, current, StringComparison.OrdinalIgnoreCase)).OrderBy(locale => locale.Position).Select(locale => locale.Code)];
    }
}

// Alt text for one of the images of the entry's media fields, into this text field; the panel asks which when there are several.
[FieldAction(AiActionNames.AltText, FieldTypes = [AiActionNames.Text, AiActionNames.LongText], Permission = AiActionNames.Use, FieldsFrom = AiActionNames.OptedInFields)]
public sealed class AltTextAction(IAdminOverlays overlays, IStringLocalizer<AiAssistResources> texts) : AiFieldAction(overlays, texts)
{
    private protected override string Name => AiActionNames.AltText;

    private protected override bool Applies(FieldActionContext context) => Images(context).Count > 0;

    private protected override AiSuggestionRequest Request(FieldActionContext context) => base.Request(context) with { Text = null, Images = Images(context) };

    // The assets of the media fields of the entry itself, not of its components and blocks.
    private static List<Guid> Images(FieldActionContext context) =>
    [
        .. context.Type.Fields
            .Where(field => field.Type == AiActionNames.Media)
            .SelectMany(field => AiFieldValues.Ids(context.Document.GetValueOrDefault(field.Name)))
            .Distinct(),
    ];
}

// The asset's own alt text, in the form's locale, for an image of this media field: saved on the asset when the user accepts
// it, as the media field's own alt text is, and never into the entry.
[FieldAction("assetAltText", FieldTypes = [AiActionNames.Media], Permission = AiActionNames.Use, FieldsFrom = AiActionNames.Assets)]
public sealed class AssetAltTextAction(IAdminOverlays overlays, IStringLocalizer<AiAssistResources> texts) : AiFieldAction(overlays, texts)
{
    private protected override string Name => AiActionNames.AltText;

    private protected override string LabelKey => "assetAltText";

    private protected override bool ForAsset => true;

    private protected override bool Applies(FieldActionContext context) => AiFieldValues.Ids(context.Value).Count > 0;

    private protected override AiSuggestionRequest Request(FieldActionContext context) =>
        base.Request(context) with { Type = null, Field = null, Text = null, Images = AiFieldValues.Ids(context.Value), ForAsset = true };
}

// What the half reads of a field's value: a text field's text, and a media field's id or ids.
internal static class AiFieldValues
{
    public static string? Text(JsonNode? value) =>
        value is JsonValue text && text.TryGetValue<string>(out var content) && !string.IsNullOrWhiteSpace(content) ? content : null;

    public static IReadOnlyList<Guid> Ids(JsonNode? value) => value switch
    {
        JsonValue single when single.TryGetValue<string>(out var text) && Guid.TryParse(text, out var id) => [id],
        JsonArray many => [.. many.Select(item => item is JsonValue one && one.TryGetValue<string>(out var text) && Guid.TryParse(text, out var id) ? id : (Guid?)null).OfType<Guid>()],
        _ => [],
    };
}
