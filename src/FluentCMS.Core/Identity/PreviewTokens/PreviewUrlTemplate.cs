using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FluentCMS.Content;
using FluentCMS.Core.Content;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Schema;

namespace FluentCMS.Core.Identity.PreviewTokens;

// A URL template of the area "preview" for one content type, with the placeholders {id}, {locale}, {token} and the name of a
// field of the type, such as {slug}. The scheme and the host are the template's alone: a placeholder stands in the path, the
// query or the fragment, and a value goes in escaped as data, so that no value of an entry adds a segment, a parameter or
// another origin. {token} is the whole value of one query parameter, which the published URL leaves out.
internal sealed partial class PreviewUrlTemplate
{
    public const int MaxLength = 2048;

    private const string IdName = "id";
    private const string LocaleName = "locale";
    private const string TokenName = "token";

    // The characters of a URL; anything else is escaped by whoever writes the template.
    private const string UrlCharacters = "-._~:/?#[]@!$&'()*+,;=%";

    // The fields whose value is one text, number, Boolean or date. A field of a plugin's type is not among them, since its value
    // could be anything.
    private static readonly FrozenSet<string> ScalarTypes = new[]
    {
        FieldTypeNames.Text, FieldTypeNames.Slug, FieldTypeNames.Select, FieldTypeNames.Email, FieldTypeNames.Url,
        FieldTypeNames.Integer, FieldTypeNames.Decimal, FieldTypeNames.Boolean, FieldTypeNames.Date, FieldTypeNames.DateTime,
    }.ToFrozenSet(StringComparer.Ordinal);

    private readonly string _draft;
    private readonly string _published;

    private PreviewUrlTemplate(string draft, string published)
    {
        _draft = draft;
        _published = published;
    }

    // Null, with the errors added under the path, when the template is not one of the type as the schema is now.
    public static PreviewUrlTemplate? Parse(string? template, SchemaType type, string path, List<ValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(errors);

        if (Form(template, out var published) is { } problem)
        {
            errors.Add(new ValidationError(path, PreviewTokenErrorCodes.InvalidUrlTemplate, problem));
            return null;
        }

        var count = errors.Count;
        foreach (var name in Placeholder().Matches(template!).Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal))
        {
            if (name is IdName or TokenName || name == LocaleName && type.Localized || IsScalarField(type, name))
            {
                continue;
            }

            errors.Add(new ValidationError(path, PreviewTokenErrorCodes.UnknownTemplateName, name == LocaleName
                ? $"{type.Name} is not localized, so its template has no {{locale}}."
                : $"{{{name}}} is not {{id}}, {{locale}}, {{token}} or a field of {type.Name} that a URL can hold: one text, slug, select, email, URL, number, Boolean or date that is not hidden."));
        }

        return errors.Count > count ? null : new PreviewUrlTemplate(template!, published!);
    }

    // The URL of the draft with the token, or of the published entry when there is no token. Null when a placeholder has no value
    // to give, such as a field that is empty, that the caller's read left out or that the schema no longer holds as one value, so a
    // URL is never half filled. A value of only dots is refused too, since a browser would read it as a step up the path.
    public string? Fill(ContentDocument entry, Guid id, string? locale, string? token)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var text = token is null ? _published : _draft;
        var url = new StringBuilder(text.Length + (token?.Length ?? 0));
        var at = 0;
        foreach (Match match in Placeholder().Matches(text))
        {
            var name = match.Groups[1].Value;
            var value = name switch
            {
                IdName => id.ToString("D"),
                LocaleName => locale,
                TokenName => token,
                _ => entry.TryGetValue(name, out var raw) ? Text(raw) : null,
            };

            if (string.IsNullOrEmpty(value) || value is "." or "..")
            {
                return null;
            }

            url.Append(text, at, match.Index - at).Append(Uri.EscapeDataString(value));
            at = match.Index + match.Length;
        }

        return url.Append(text, at, text.Length - at).ToString();
    }

    // What is wrong with the form of the template, or null; published is the template without the parameter of the token.
    private static string? Form(string? template, out string? published)
    {
        published = null;
        if (string.IsNullOrEmpty(template) || template.Length > MaxLength)
        {
            return $"A template is an absolute http or https URL of at most {MaxLength} characters.";
        }

        if (!template.All(c => char.IsAsciiLetterOrDigit(c) || c is '{' or '}' || UrlCharacters.Contains(c, StringComparison.Ordinal)))
        {
            return "A template holds the characters of a URL only; escape any other, such as a space as %20.";
        }

        if (Placeholder().Replace(template, "").AsSpan().IndexOfAny('{', '}') >= 0)
        {
            return "A brace opens a placeholder, which is a name of letters and digits such as {slug}, and closes it.";
        }

        // The host is the template's own, so that no value of an entry can send the URL, or the token, somewhere else.
        var scheme = template.IndexOf("://", StringComparison.Ordinal);
        var rest = scheme < 0 ? "" : template[(scheme + 3)..];
        var end = rest.IndexOfAny(['/', '?', '#']);
        var authority = end < 0 ? rest : rest[..end];
        if (scheme < 0
            || !template[..scheme].Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && !template[..scheme].Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || authority.Length == 0
            || authority.AsSpan().IndexOfAny('{', '@') >= 0
            || !Uri.TryCreate(Placeholder().Replace(template, "x"), UriKind.Absolute, out var sample)
            || sample.Scheme is not ("http" or "https")
            || sample.Host.Length == 0)
        {
            return "A template is an absolute http or https URL with a host of its own, no user name, and placeholders in its path, query or fragment only.";
        }

        var fragment = template.IndexOf('#', StringComparison.Ordinal);
        var query = template.IndexOf('?', StringComparison.Ordinal);
        if (fragment >= 0 && query > fragment)
        {
            query = -1;
        }

        var parameters = query < 0 ? [] : template[(query + 1)..(fragment < 0 ? template.Length : fragment)].Split('&');
        var token = Array.FindIndex(parameters, IsTokenParameter);
        if (token < 0 || Placeholder().Matches(template).Count(match => match.Groups[1].Value == TokenName) != 1)
        {
            return "{token} is the whole value of one query parameter, such as ?preview={token}, and is there once.";
        }

        var kept = parameters.Where((_, index) => index != token).ToList();
        published = template[..query] + (kept.Count > 0 ? "?" + string.Join('&', kept) : "") + (fragment < 0 ? "" : template[fragment..]);
        return null;
    }

    private static bool IsScalarField(SchemaType type, string name) =>
        type.FindField(name) is { Hidden: false, Multiple: false, Relation: null } field && ScalarTypes.Contains(field.FieldType);

    // name={token}, with a name of its own.
    private static bool IsTokenParameter(string parameter)
    {
        var equals = parameter.IndexOf('=', StringComparison.Ordinal);
        return equals > 0 && parameter.IndexOf('{', StringComparison.Ordinal) == equals + 1 && parameter[(equals + 1)..] == "{" + TokenName + "}";
    }

    private static string? Text(object? value) => value switch
    {
        string text => text,
        bool flag => flag ? "true" : "false",
        DateOnly day => EntryJson.Date(day),
        DateTimeOffset moment => EntryJson.Moment(moment),
        int or long or decimal => Convert.ToString(value, CultureInfo.InvariantCulture),
        _ => null,
    };

    [GeneratedRegex("\\{([a-z][a-zA-Z0-9]*)\\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();
}
