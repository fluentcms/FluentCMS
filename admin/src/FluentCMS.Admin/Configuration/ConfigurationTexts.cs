namespace FluentCMS.Admin.Configuration;

// Every text of the configuration error page, in one place, until localization moves them into resources (A-05).
internal static class ConfigurationTexts
{
    public const string Title = "The admin cannot start";

    public const string Introduction = "The configuration file config.json of this admin has a problem. Correct the file and reload the page.";

    private const string Ascii = "in ASCII only; write an internationalized host name in its xn-- form";

    public static string Describe(ConfigurationProblem problem) => problem.Kind switch
    {
        ConfigurationProblemKind.Unreadable => problem.StatusCode is { } status
            ? $"config.json could not be read: the server answered with status {status}."
            : "config.json could not be read: the server did not answer in time, or its answer could not be decoded.",
        ConfigurationProblemKind.NotJson => "config.json is not valid JSON.",
        ConfigurationProblemKind.NotAnObject => "config.json must hold a JSON object.",
        ConfigurationProblemKind.Missing => $"{problem.Member} is required.",
        ConfigurationProblemKind.Duplicate => $"{problem.Member} appears more than once; keep one.",
        _ => $"{problem.Member} {Rule(problem.Rule)}.",
    };

    private static string Rule(ConfigurationRule rule) => rule switch
    {
        ConfigurationRule.HttpUrl => $"must be an absolute http or https URL without user info, query or fragment, {Ascii}",
        ConfigurationRule.UrlOrPath => $"must be an absolute http or https URL or a relative path, without a fragment, spaces, quotes, parentheses or backslashes, {Ascii}",
        ConfigurationRule.Origin => $"must be an origin, an http or https URL with no path such as https://www.example.com, {Ascii}",
        ConfigurationRule.HexColor => "must be a hex color such as #0f6cbd",
        ConfigurationRule.Text => "must be a text that is not empty, without spaces at its start or end and without control characters",
        ConfigurationRule.ClientId => "must be a client id of visible ASCII characters without spaces",
        ConfigurationRule.ScopeList => "must be a list of distinct scopes, such as [\"openid\", \"management\"]",
        ConfigurationRule.CultureName => "must be a culture name such as en or de-CH",
        ConfigurationRule.CultureList => "must be a list of distinct culture names that is not empty",
        ConfigurationRule.DefaultCultureOffered => "must be one of cultures.offered (it is en when not set)",
        ConfigurationRule.OriginList => "must be a list of origins",
        ConfigurationRule.Object => "must be an object",
        _ => "is not valid",
    };
}
