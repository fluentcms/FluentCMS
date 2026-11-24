using System.ComponentModel;
using FluentCMS.Content;
using FluentCMS.Core.Settings;
using FluentCMS.Schema;
using FluentCMS.Settings;

namespace FluentCMS.Core.Identity.PreviewTokens;

// The settings area "preview" (settings.preview.manage), per site with instance defaults: the page of a front end that shows
// an entry of a type. A preview token's answer fills it in, so an editor who mints a token gets the page without reading the area.
[Settings(Area)]
internal sealed class PreviewSettings
{
    public const string Area = "preview";

    [Description("An absolute http or https URL template per content type, such as https://www.example.com/{locale}/articles/{slug}?preview={token}. {id}, {locale}, {token} and a field of the type are filled in, escaped. {token} is the whole value of one query parameter, which the published URL leaves out.")]
    public Dictionary<string, string> Templates { get; set; } = [];

    // The type's template. One that the type no longer fits, since it was saved, is none.
    public PreviewUrlTemplate? TemplateOf(SchemaType type) =>
        Templates.TryGetValue(type.Name, out var template) ? PreviewUrlTemplate.Parse(template, type, "", []) : null;
}

// The rules of the area that need the schema: each template is for a content type, and a template of that type.
internal sealed class PreviewSettingsCheck(SchemaModel schema) : ISettingsCheck
{
    public string Area => PreviewSettings.Area;

    public IEnumerable<ValidationError> Check(object value)
    {
        List<ValidationError> errors = [];
        foreach (var (name, template) in ((PreviewSettings)value).Templates.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var path = $"templates.{name}";
            if (schema.FindContentType(name) is { } type)
            {
                PreviewUrlTemplate.Parse(template, type, path, errors);
            }
            else
            {
                errors.Add(new ValidationError(path, PreviewTokenErrorCodes.UnknownTemplateName, $"There is no content type '{name}'."));
            }
        }

        return errors;
    }
}
