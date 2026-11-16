using System.Text.Json;
using System.Text.RegularExpressions;

namespace FluentCMS.Admin.Configuration;

// Reads config.json by walking a JsonDocument rather than through a serializer, so that it trims without a source-generated
// context, names every member that is wrong, and sees the members it does not know.
internal static partial class AdminConfigurationReader
{
    public static AdminStartup Read(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return new AdminStartup([new ConfigurationProblem(ConfigurationProblemKind.NotJson)], []);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new AdminStartup([new ConfigurationProblem(ConfigurationProblemKind.NotAnObject)], []);
            }

            var context = new Context();
            var configuration = ReadRoot(document.RootElement, context);
            return configuration is not null && context.Problems.Count == 0
                ? new AdminStartup(configuration, context.Unknown)
                : new AdminStartup(context.Problems, context.Unknown);
        }
    }

    private static AdminConfiguration? ReadRoot(JsonElement root, Context context)
    {
        Uri? apiBaseUrl = null;
        Uri? authority = null;
        string? clientId = null;
        IReadOnlyList<string>? scopes = null;
        AdminBranding? branding = null;
        AdminCultures? cultures = null;
        IReadOnlyList<string>? previewOrigins = null;

        foreach (var property in root.EnumerateObject())
        {
            var value = property.Value;
            switch (property.Name)
            {
                case "apiBaseUrl":
                    apiBaseUrl = ReadBaseUrl(value, "apiBaseUrl", context);
                    break;
                case "authority":
                    authority = ReadBaseUrl(value, "authority", context);
                    break;
                case "clientId":
                    clientId = ReadString(value, "clientId", ConfigurationRule.ClientId, ClientIdPattern(), context);
                    break;
                case "scopes":
                    scopes = ReadScopes(value, context);
                    break;
                case "branding":
                    branding = ReadBranding(value, context);
                    break;
                case "cultures":
                    cultures = ReadCultures(value, context);
                    break;
                case "previewOrigins":
                    previewOrigins = ReadPreviewOrigins(value, context);
                    break;
                default:
                    context.Unknown.Add(property.Name);
                    break;
            }
        }

        if (apiBaseUrl is null && !context.HasProblem("apiBaseUrl"))
        {
            context.Missing("apiBaseUrl");
        }

        if (apiBaseUrl is null || context.Problems.Count > 0)
        {
            return null;
        }

        var configuration = new AdminConfiguration { ApiBaseUrl = apiBaseUrl };
        if (authority is not null)
        {
            configuration = configuration with { Authority = authority };
        }

        if (clientId is not null)
        {
            configuration = configuration with { ClientId = clientId };
        }

        if (scopes is not null)
        {
            configuration = configuration with { Scopes = scopes };
        }

        if (branding is not null)
        {
            configuration = configuration with { Branding = branding };
        }

        if (cultures is not null)
        {
            configuration = configuration with { Cultures = cultures };
        }

        if (previewOrigins is not null)
        {
            configuration = configuration with { PreviewOrigins = previewOrigins };
        }

        return configuration;
    }

    private static AdminBranding? ReadBranding(JsonElement value, Context context)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            context.Invalid("branding", ConfigurationRule.Object);
            return null;
        }

        var branding = new AdminBranding();
        foreach (var property in value.EnumerateObject())
        {
            var member = "branding." + property.Name;
            switch (property.Name)
            {
                case "name":
                    var name = ReadString(property.Value, member, ConfigurationRule.Text, TextPattern(), context);
                    branding = name is null ? branding : branding with { Name = name };
                    break;
                case "logoUrl":
                    branding = branding with { LogoUrl = ReadUrlOrPath(property.Value, member, context) };
                    break;
                case "faviconUrl":
                    branding = branding with { FaviconUrl = ReadUrlOrPath(property.Value, member, context) };
                    break;
                case "accentColor":
                    branding = branding with
                    {
                        AccentColor = ReadString(property.Value, member, ConfigurationRule.HexColor, HexColorPattern(), context),
                    };
                    break;
                default:
                    context.Unknown.Add(member);
                    break;
            }
        }

        return branding;
    }

    private static AdminCultures? ReadCultures(JsonElement value, Context context)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            context.Invalid("cultures", ConfigurationRule.Object);
            return null;
        }

        var cultures = new AdminCultures();
        foreach (var property in value.EnumerateObject())
        {
            switch (property.Name)
            {
                case "offered":
                    cultures = cultures with { Offered = ReadOfferedCultures(property.Value, context) };
                    break;
                case "default":
                    var culture = ReadString(property.Value, "cultures.default", ConfigurationRule.CultureName, CultureNamePattern(), context);
                    cultures = culture is null ? cultures : cultures with { Default = culture };
                    break;
                default:
                    context.Unknown.Add("cultures." + property.Name);
                    break;
            }
        }

        if (cultures.Offered is { } offered
            && !context.HasProblem("cultures.default")
            && !offered.Contains(cultures.Default, StringComparer.OrdinalIgnoreCase))
        {
            context.Invalid("cultures.default", ConfigurationRule.DefaultCultureOffered);
        }

        return cultures;
    }

    private static List<string>? ReadOfferedCultures(JsonElement value, Context context)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
        {
            context.Invalid("cultures.offered", ConfigurationRule.CultureList);
            return null;
        }

        var offered = new List<string>();
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            var culture = ReadString(item, $"cultures.offered[{index++}]", ConfigurationRule.CultureName, CultureNamePattern(), context, allowNull: false);
            if (culture is not null)
            {
                offered.Add(culture);
            }
        }

        if (offered.Distinct(StringComparer.OrdinalIgnoreCase).Count() != offered.Count)
        {
            context.Invalid("cultures.offered", ConfigurationRule.CultureList);
        }

        return offered;
    }

    private static List<string>? ReadScopes(JsonElement value, Context context)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var scopes = new List<string>();
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { } scope && ScopePattern().IsMatch(scope))
                {
                    scopes.Add(scope);
                }
                else
                {
                    scopes.Clear();
                    break;
                }
            }
        }

        if (scopes.Count == 0 || scopes.Distinct(StringComparer.Ordinal).Count() != scopes.Count)
        {
            context.Invalid("scopes", ConfigurationRule.ScopeList);
            return null;
        }

        return scopes;
    }

    private static List<string>? ReadPreviewOrigins(JsonElement value, Context context)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            context.Invalid("previewOrigins", ConfigurationRule.OriginList);
            return null;
        }

        var origins = new List<string>();
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            var member = $"previewOrigins[{index++}]";
            var uri = item.ValueKind == JsonValueKind.String ? ParseHttpUrl(item.GetString()) : null;
            if (uri is null || uri.AbsolutePath != "/")
            {
                context.Invalid(member, ConfigurationRule.Origin);
                continue;
            }

            var origin = uri.GetLeftPart(UriPartial.Authority);
            if (!origins.Contains(origin, StringComparer.Ordinal))
            {
                origins.Add(origin);
            }
        }

        return origins;
    }

    private static Uri? ReadBaseUrl(JsonElement value, string member, Context context)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var uri = value.ValueKind == JsonValueKind.String ? ParseHttpUrl(value.GetString()) : null;
        if (uri is null)
        {
            context.Invalid(member, ConfigurationRule.HttpUrl);
            return null;
        }

        // Always with a trailing slash: without it, a relative address such as api/management/v1/ would replace the last
        // segment of a path base like https://example.com/cms instead of resolving under it.
        return new Uri(uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/", UriKind.Absolute);
    }

    private static Uri? ReadUrlOrPath(JsonElement value, string member, Context context)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        Uri? uri = null;
        if (!string.IsNullOrEmpty(text) && !HasUnsafeCharacter(text) && !text.Contains('#', StringComparison.Ordinal))
        {
            if (SchemePattern().IsMatch(text))
            {
                uri = Uri.TryCreate(text, UriKind.Absolute, out var absolute)
                    && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps)
                    && absolute.Host.Length > 0
                    && absolute.UserInfo.Length == 0
                        ? absolute
                        : null;
            }
            else if (!text.StartsWith("//", StringComparison.Ordinal) && Uri.TryCreate(text, UriKind.Relative, out var relative))
            {
                uri = relative;
            }
        }

        if (uri is null)
        {
            context.Invalid(member, ConfigurationRule.UrlOrPath);
        }

        return uri;
    }

    private static string? ReadString(JsonElement value, string member, ConfigurationRule rule, Regex pattern, Context context, bool allowNull = true)
    {
        if (allowNull && value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String && value.GetString() is { } text && pattern.IsMatch(text))
        {
            return text;
        }

        context.Invalid(member, rule);
        return null;
    }

    // An absolute http or https URL with a host and without user info, query or fragment.
    private static Uri? ParseHttpUrl(string? text)
    {
        if (string.IsNullOrEmpty(text) || HasUnsafeCharacter(text) || text.AsSpan().IndexOfAny("?#@") >= 0)
        {
            return null;
        }

        return Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && uri.Host.Length > 0
            && text.StartsWith(uri.Scheme + "://", StringComparison.OrdinalIgnoreCase)
                ? uri
                : null;
    }

    // Spaces, control and non-ASCII characters, quotes, parentheses, angle brackets and backslashes are refused in every URL,
    // so that a URL can be put in an HTML attribute or a CSS url("...") without escaping.
    private static bool HasUnsafeCharacter(string text)
    {
        foreach (var character in text)
        {
            if (character <= ' ' || character > '~' || "\"'()<>\\`".Contains(character, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})\z")]
    private static partial Regex HexColorPattern();

    [GeneratedRegex(@"^[A-Za-z]{2,8}(?:-[A-Za-z0-9]{1,8})*\z")]
    private static partial Regex CultureNamePattern();

    // The scope-token of RFC 6749, section 3.3.
    [GeneratedRegex(@"^[\x21\x23-\x5B\x5D-\x7E]+\z")]
    private static partial Regex ScopePattern();

    // The VSCHAR of RFC 6749, appendix A, without the space.
    [GeneratedRegex(@"^[\x21-\x7E]+\z")]
    private static partial Regex ClientIdPattern();

    [GeneratedRegex(@"^(?!\s)[^\p{Cc}]+(?<!\s)\z")]
    private static partial Regex TextPattern();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9+.-]*:")]
    private static partial Regex SchemePattern();

    private sealed class Context
    {
        public List<ConfigurationProblem> Problems { get; } = [];

        public List<string> Unknown { get; } = [];

        public bool HasProblem(string member) => Problems.Exists(problem => problem.Member == member);

        public void Missing(string member) => Problems.Add(new ConfigurationProblem(ConfigurationProblemKind.Missing, member));

        public void Invalid(string member, ConfigurationRule rule) =>
            Problems.Add(new ConfigurationProblem(ConfigurationProblemKind.Invalid, member, rule));
    }
}
