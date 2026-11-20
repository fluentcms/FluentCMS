namespace FluentCMS.Admin.Configuration;

internal enum ConfigurationProblemKind
{
    Unreadable,
    NotJson,
    NotAnObject,
    Missing,
    Invalid,
    Duplicate,
}

// What a member must be; ConfigurationTexts turns it into the sentence of the error page.
internal enum ConfigurationRule
{
    None,
    HttpUrl,
    UrlOrPath,
    Origin,
    HexColor,
    Text,
    ClientId,
    ScopeList,
    CultureName,
    CultureList,
    DefaultCultureOffered,
    OriginList,
    NotAdminOrigin,
    Object,
}

// Member is the path in config.json, such as branding.accentColor or previewOrigins[1]; null for a problem of the whole file.
// StatusCode is the HTTP status of an unreadable file, when there was a response.
internal sealed record ConfigurationProblem(
    ConfigurationProblemKind Kind,
    string? Member = null,
    ConfigurationRule Rule = ConfigurationRule.None,
    int? StatusCode = null);
