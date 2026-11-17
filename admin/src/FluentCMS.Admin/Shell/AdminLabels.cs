using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;
using Microsoft.Extensions.Localization;

namespace FluentCMS.Admin.Shell;

// The labels of types and entries (spec 4.1, AQ10). The schema's displayName and titleField come first where they apply;
// without them the label is derived.
internal static class AdminLabels
{
    private const string TextField = "text";

    // The app's resource ContentType.{name}, then the type's displayName, then its name made readable.
    public static string Type(SchemaTypeDescription type, IStringLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(localizer);

        var resource = localizer[$"ContentType.{type.Name}"];
        if (!resource.ResourceNotFound)
        {
            return resource.Value;
        }

        return string.IsNullOrWhiteSpace(type.DisplayName) ? Readable(type.Name) : type.DisplayName;
    }

    // A kebab-case name as words, with a capital first letter: site-settings is "Site settings".
    public static string Readable(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var words = name.Replace('-', ' ').Trim();
        return words.Length == 0 ? name : char.ToUpperInvariant(words[0]) + words[1..];
    }

    // The field's display.label, or its camelCase name as words: publishedOn is "Published on".
    public static string Field(SchemaFieldDescription field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (!string.IsNullOrWhiteSpace(field.Display?.Label))
        {
            return field.Display.Label;
        }

        var words = new System.Text.StringBuilder(field.Name.Length + 4);
        foreach (var character in field.Name)
        {
            if (char.IsUpper(character) && words.Length > 0)
            {
                words.Append(' ').Append(char.ToLowerInvariant(character));
            }
            else
            {
                words.Append(character);
            }
        }

        return Readable(words.ToString());
    }

    // The first that has a value: the titleField, a text field named title or name, the source field of the slug, the first
    // required text field; else the id.
    public static string Entry(SchemaTypeDescription type, ContentEntry entry)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(entry);

        foreach (var field in Candidates(type))
        {
            if (entry.Fields[field] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return entry.Id.ToString("D");
    }

    // The field a list or a picker reads for the label: the first of the fields above that the type has and does not hide,
    // since a read leaves a hidden field out, or null when it has none.
    public static string? LabelField(SchemaTypeDescription type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return Candidates(type).FirstOrDefault(name => type.Fields.Any(field => field.Name == name && !field.Hidden));
    }

    private static IEnumerable<string> Candidates(SchemaTypeDescription type)
    {
        if (!string.IsNullOrEmpty(type.TitleField))
        {
            yield return type.TitleField;
        }

        var texts = type.Fields.Where(field => field.Type == TextField && !field.Multiple).ToList();
        foreach (var named in new[] { "title", "name" })
        {
            if (texts.FirstOrDefault(field => field.Name == named) is { } match)
            {
                yield return match.Name;
            }
        }

        foreach (var slug in type.Fields.Where(field => field.Type == "slug"))
        {
            if (slug.Settings["source"] is JsonValue source && source.TryGetValue<string>(out var name))
            {
                yield return name;
            }
        }

        if (texts.FirstOrDefault(field => field.Validation.Required) is { } required)
        {
            yield return required.Name;
        }
    }
}
