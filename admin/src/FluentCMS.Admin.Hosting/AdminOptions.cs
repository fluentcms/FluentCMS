using System.Text.RegularExpressions;
using FluentCMS.Core.DependencyInjection;

namespace FluentCMS.Admin.Hosting;

// FluentCms:Admin, the admin that MapFluentCmsAdmin serves at /admin and the config.json it builds. Each value is checked
// at start with the rules the admin reads config.json with (admin spec 2.5), so that a value the admin would refuse stops
// the host instead of showing the admin's error page.
public sealed partial class AdminOptions
{
    public const string Section = "Admin";

    // The root of FluentCMS's configuration and the section of cms.Clients, which the admin's refusals name.
    internal const string Root = "FluentCms";
    internal const string ClientsSection = "Clients";

    // Off until the admin is released (A-47): off, nothing is served under /admin, no client is registered, and the one
    // registered while it was on is deleted at start.
    public bool Enabled { get; set; }

    // The origin and path base of the API, such as https://cms.example.com. Empty, it is FluentCms:PublicOrigin, or the
    // request's in Development.
    public string? ApiBaseUrl { get; set; }

    public AdminBrandingOptions Branding { get; } = new();

    public AdminCultureOptions Cultures { get; } = new();

    // The origins the preview frame may load, such as https://www.example.com; none turns preview off.
    public IList<string> PreviewOrigins { get; } = [];

    internal void Validate()
    {
        if (!string.IsNullOrEmpty(ApiBaseUrl))
        {
            Require(HttpUrl(ApiBaseUrl) is not null, $"{nameof(ApiBaseUrl)} is '{ApiBaseUrl}'; it must be an absolute http or https URL such as https://cms.example.com, with no user, query or fragment.");
        }

        Branding.Validate($"{nameof(Branding)}:");
        Cultures.Validate($"{nameof(Cultures)}:");
        for (var index = 0; index < PreviewOrigins.Count; index++)
        {
            Require(Origin(PreviewOrigins[index]) is not null, $"{nameof(PreviewOrigins)}:{index} is '{PreviewOrigins[index]}'; it must be an http or https origin with no path, such as https://www.example.com.");
        }
    }

    // An absolute http or https URL with a host and no user info, query or fragment, in the characters config.json allows.
    internal static Uri? HttpUrl(string? text) =>
        !string.IsNullOrEmpty(text)
        && !HasUnsafeCharacter(text)
        && text.AsSpan().IndexOfAny("?#@") < 0
        && Uri.TryCreate(text, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && uri.Host.Length > 0
        && text.StartsWith(uri.Scheme + "://", StringComparison.OrdinalIgnoreCase)
            ? uri
            : null;

    // The origin of an http or https URL without a path, as config.json writes it.
    internal static string? Origin(string? text) =>
        HttpUrl(text) is { AbsolutePath: "/" } uri ? uri.GetLeftPart(UriPartial.Authority) : null;

    // Spaces, control and non-ASCII characters, quotes, parentheses, angle brackets and backslashes, which the admin refuses
    // in a URL.
    internal static bool HasUnsafeCharacter(string text) =>
        text.Any(character => character <= ' ' || character > '~' || "\"'()<>\\`".Contains(character, StringComparison.Ordinal));

    internal static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw FluentCmsConfigurationException.InvalidOption(message);
        }
    }

    [GeneratedRegex(@"^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})\z")]
    internal static partial Regex HexColorPattern();

    [GeneratedRegex(@"^[A-Za-z]{2,8}(?:-[A-Za-z0-9]{1,8})*\z")]
    internal static partial Regex CultureNamePattern();

    [GeneratedRegex(@"^(?!\s)[^\p{Cc}]+(?<!\s)\z")]
    internal static partial Regex TextPattern();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9+.-]*:")]
    internal static partial Regex SchemePattern();

    // The VSCHAR of RFC 6749, appendix A, without the space.
    [GeneratedRegex(@"^[\x21-\x7E]+\z")]
    internal static partial Regex ClientIdPattern();
}

// The admin's name, logo, favicon and accent color. A logo or favicon is an absolute http or https URL, which the page's
// img-src then allows, or a path relative to the admin's base.
public sealed class AdminBrandingOptions
{
    public string? Name { get; set; }

    public string? LogoUrl { get; set; }

    public string? FaviconUrl { get; set; }

    // A hex color: #rgb, #rgba, #rrggbb or #rrggbbaa.
    public string? AccentColor { get; set; }

    internal void Validate(string prefix)
    {
        if (!string.IsNullOrEmpty(Name))
        {
            AdminOptions.Require(AdminOptions.TextPattern().IsMatch(Name), $"{prefix}{nameof(Name)} has a control character, or a space at its start or end.");
        }

        ValidateUrlOrPath(LogoUrl, $"{prefix}{nameof(LogoUrl)}");
        ValidateUrlOrPath(FaviconUrl, $"{prefix}{nameof(FaviconUrl)}");
        if (!string.IsNullOrEmpty(AccentColor))
        {
            AdminOptions.Require(AdminOptions.HexColorPattern().IsMatch(AccentColor), $"{prefix}{nameof(AccentColor)} is '{AccentColor}'; it must be a hex color such as #0f6cbd.");
        }
    }

    // The absolute URLs among the logo and the favicon, whose origins the page's img-src allows, by the test that admits them.
    internal IEnumerable<Uri> AbsoluteUrls() =>
        new[] { LogoUrl, FaviconUrl }.Select(value => IsUrlOrPath(value, out var absolute) ? absolute : null).OfType<Uri>();

    private static void ValidateUrlOrPath(string? value, string key)
    {
        if (!string.IsNullOrEmpty(value))
        {
            AdminOptions.Require(IsUrlOrPath(value, out _), $"{key} is '{value}'; it must be an absolute http or https URL, or a path relative to the admin such as images/logo.svg.");
        }
    }

    // The admin's rule for a logo or favicon: an absolute http or https URL without user info, which may carry a query, or a
    // relative path; absolute is the URL when it is one.
    private static bool IsUrlOrPath(string? value, out Uri? absolute)
    {
        absolute = null;
        if (string.IsNullOrEmpty(value) || AdminOptions.HasUnsafeCharacter(value) || value.Contains('#', StringComparison.Ordinal))
        {
            return false;
        }

        if (!AdminOptions.SchemePattern().IsMatch(value))
        {
            return !value.StartsWith("//", StringComparison.Ordinal) && Uri.TryCreate(value, UriKind.Relative, out _);
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && uri.Host.Length > 0
            && uri.UserInfo.Length == 0)
        {
            absolute = uri;
            return true;
        }

        return false;
    }
}

// The cultures the admin offers its users, and the one it starts in. Empty, it offers every culture it ships, and starts
// in en.
public sealed class AdminCultureOptions
{
    public IList<string> Offered { get; } = [];

    // One of Offered when that is set. Empty, it is en.
    public string? Default { get; set; }

    internal void Validate(string prefix)
    {
        for (var index = 0; index < Offered.Count; index++)
        {
            AdminOptions.Require(AdminOptions.CultureNamePattern().IsMatch(Offered[index] ?? ""), $"{prefix}{nameof(Offered)}:{index} is '{Offered[index]}'; it must be a culture name such as en or fr-CA.");
        }

        AdminOptions.Require(Offered.Distinct(StringComparer.OrdinalIgnoreCase).Count() == Offered.Count, $"{prefix}{nameof(Offered)} names a culture twice.");
        var start = string.IsNullOrEmpty(Default) ? "en" : Default;
        AdminOptions.Require(AdminOptions.CultureNamePattern().IsMatch(start), $"{prefix}{nameof(Default)} is '{Default}'; it must be a culture name such as en or fr-CA.");
        AdminOptions.Require(Offered.Count == 0 || Offered.Contains(start, StringComparer.OrdinalIgnoreCase), $"{prefix}{nameof(Default)} is '{start}', which {nameof(Offered)} does not name.");
    }
}
