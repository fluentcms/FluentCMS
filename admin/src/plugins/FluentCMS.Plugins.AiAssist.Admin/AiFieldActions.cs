using System.Globalization;
using System.Text.Json.Nodes;
using FluentCMS.Admin;
using FluentCMS.Admin.Actions;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Plugins;
using FluentCMS.Plugins.AiAssist.Admin.Api;
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
// user accepted into the draft, or for translate into the draft of the locale chosen, as an unsaved change; anything else
// leaves the draft as it was. It saves nothing.
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

    private protected IStringLocalizer<AiAssistResources> Texts => _texts;

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
        var accepted = await new AiSuggestions(_overlays).AskAsync(await RequestAsync(context, cancellationToken), cancellationToken);
        return accepted is null || ForAsset ? FieldActionResult.Unchanged : Result(accepted);
    }

    private protected virtual FieldActionResult Result(AiAccepted accepted) => FieldActionResult.Replace(JsonValue.Create(accepted.Text));

    // A text action works on the field's text, so a field without one has nothing to offer it.
    private protected virtual bool Applies(FieldActionContext context) => AiFieldValues.Text(context.Value) is not null;

    private protected virtual ValueTask<AiSuggestionRequest> RequestAsync(FieldActionContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Request(context));

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

// The field's text in another enabled locale of the site, for an entry that is saved, of a localized type. The panel offers
// the locales where the caller may change the field, and an accepted translation goes into that locale's draft: the form
// moves there with it as an unsaved change, and the field in the locale shown keeps its text.
[FieldAction(AiActionNames.Translate, FieldTypes = [AiActionNames.Text, AiActionNames.LongText], Permission = AiActionNames.Use, FieldsFrom = AiActionNames.OptedInFields)]
public sealed class TranslateAction(IAdminOverlays overlays, IStringLocalizer<AiAssistResources> texts, IAdminContext admin, IManagementApi api) : AiFieldAction(overlays, texts)
{
    private protected override string Name => AiActionNames.Translate;

    public override async ValueTask<bool> AppliesToAsync(FieldActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Applies(context) && (await TargetsAsync(context, cancellationToken)).Count > 0;
    }

    private protected override async ValueTask<AiSuggestionRequest> RequestAsync(FieldActionContext context, CancellationToken cancellationToken) =>
        base.Request(context) with { Targets = await TargetsAsync(context, cancellationToken) };

    private protected override FieldActionResult Result(AiAccepted accepted) =>
        accepted.Locale is { } locale ? FieldActionResult.ReplaceIn(locale, JsonValue.Create(accepted.Text)) : FieldActionResult.Unchanged;

    // The site's enabled locales in its order, but the one shown, where GET /me/access says the caller may change the field.
    private async Task<IReadOnlyList<AiTargetLocale>> TargetsAsync(FieldActionContext context, CancellationToken cancellationToken)
    {
        if (context.ContentLocale is not { } shown || context.EntryId is null || admin.Site is not { } site)
        {
            return [];
        }

        var ai = new AiApi(api);
        List<AiTargetLocale> found = [];
        foreach (var locale in site.Locales.Where(item => item.Enabled && !string.Equals(item.Code, shown, StringComparison.OrdinalIgnoreCase)).OrderBy(item => item.Position))
        {
            if (await ai.MayUpdateAsync(context.Type.Name, context.Field.Name, locale.Code, cancellationToken))
            {
                found.Add(new AiTargetLocale(locale.Code, NameOf(locale.Code)));
            }
        }

        return found;
    }

    // The language in itself beside its code, as the form's locale switcher names it.
    private string NameOf(string code)
    {
        try
        {
            return Texts["Ai.Panel.LocaleName", code, CultureInfo.GetCultureInfo(code, predefinedOnly: true).NativeName];
        }
        catch (CultureNotFoundException)
        {
            return code;
        }
    }
}

// Alt text for one of the images of the entry's media fields, into this text field; the panel asks which when there are several.
// The server describes only an image that the saved draft refers to in the form's locale, so the images are those of the draft
// as it was saved, and an entry that is not saved, in that locale or at all, has none. It also asks the right to update the
// image, so a caller to whom GET /ai/fields answers no assets, such as the built-in Author, has none either.
[FieldAction(AiActionNames.AltText, FieldTypes = [AiActionNames.Text, AiActionNames.LongText], Permission = AiActionNames.Use, FieldsFrom = AiActionNames.OptedInFields)]
public sealed class AltTextAction(IAdminOverlays overlays, IStringLocalizer<AiAssistResources> texts, IManagementApi api) : AiFieldAction(overlays, texts)
{
    private protected override string Name => AiActionNames.AltText;

    public override async ValueTask<bool> AppliesToAsync(FieldActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return (await ImagesAsync(context, cancellationToken)).Count > 0;
    }

    private protected override async ValueTask<AiSuggestionRequest> RequestAsync(FieldActionContext context, CancellationToken cancellationToken) =>
        base.Request(context) with { Text = null, DocumentId = context.EntryId, Images = await ImagesAsync(context, cancellationToken) };

    // The assets of the media fields of the entry itself, not of its components and blocks.
    private async Task<IReadOnlyList<Guid>> ImagesAsync(FieldActionContext context, CancellationToken cancellationToken)
    {
        var fields = context.Type.Fields.Where(field => field.Type == AiActionNames.Media).Select(field => field.Name).ToList();
        var ai = new AiApi(api);
        if (context.EntryId is not { } id || fields.Count == 0 || !await ai.AssetsAsync(cancellationToken)
            || await ai.DraftAsync(context.Type.Name, id, fields, context.ContentLocale, cancellationToken) is not { } draft)
        {
            return [];
        }

        return [.. fields.SelectMany(field => AiFieldValues.Ids(draft[field])).Distinct()];
    }
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

// What the half reads of a field's value: a text field's text, and a media field's id or ids, as the form holds them or as
// a read gives them, { "id", "type" }.
internal static class AiFieldValues
{
    public static string? Text(JsonNode? value) =>
        value is JsonValue text && text.TryGetValue<string>(out var content) && !string.IsNullOrWhiteSpace(content) ? content : null;

    public static IReadOnlyList<Guid> Ids(JsonNode? value) => value switch
    {
        JsonArray many => [.. many.Select(Id).OfType<Guid>()],
        _ when Id(value) is { } id => [id],
        _ => [],
    };

    private static Guid? Id(JsonNode? value) => value switch
    {
        JsonValue one when one.TryGetValue<string>(out var text) && Guid.TryParse(text, out var id) => id,
        JsonObject read => Id(read["id"]),
        _ => null,
    };
}
