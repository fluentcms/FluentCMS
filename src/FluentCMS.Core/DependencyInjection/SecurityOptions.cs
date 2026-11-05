namespace FluentCMS.Core.DependencyInjection;

// FluentCms:Security.
public sealed class SecurityOptions
{
    public const string Section = "Security";

    private static readonly string[] ReferrerPolicies =
    [
        "no-referrer",
        "no-referrer-when-downgrade",
        "origin",
        "origin-when-cross-origin",
        "same-origin",
        "strict-origin",
        "strict-origin-when-cross-origin",
        "unsafe-url",
    ];

    // Lets Production start with the signing key the authorization server generates when no certificate is configured.
    public bool AllowGeneratedSigningKey { get; set; }

    public SecurityCorsOptions Cors { get; } = new();

    // The Referrer-Policy header of FluentCMS responses.
    public string ReferrerPolicy { get; set; } = "no-referrer";

    // The sources that may frame the hosted pages, as the frame-ancestors directive takes them. None by default.
    public string FrameAncestors { get; set; } = "'none'";

    internal void Validate()
    {
        if (!ReferrerPolicies.Contains(ReferrerPolicy))
        {
            throw new FluentCmsConfigurationException(
                HostErrorCodes.InvalidOption,
                $"{nameof(ReferrerPolicy)} is '{ReferrerPolicy}'; it must be one of {string.Join(", ", ReferrerPolicies)}.");
        }

        if (string.IsNullOrWhiteSpace(FrameAncestors) || FrameAncestors.Any(character => char.IsControl(character) || character is ';' or ','))
        {
            throw new FluentCmsConfigurationException(
                HostErrorCodes.InvalidOption,
                $"{nameof(FrameAncestors)} is '{FrameAncestors}'; it must be a space-separated list of sources such as 'none', 'self' or https://admin.example.com, without a semicolon or comma.");
        }

        Cors.Delivery.Validate("Cors:Delivery");
        Cors.Management.Validate("Cors:Management");
    }
}

public sealed class SecurityCorsOptions
{
    public CorsOriginsOptions Delivery { get; } = new();

    public CorsOriginsOptions Management { get; } = new();
}

// The browser origins one API answers. Empty, the default, answers none, and a wildcard is refused.
public sealed class CorsOriginsOptions
{
    public IList<string> AllowedOrigins { get; } = [];

    internal void Validate(string key)
    {
        for (var index = 0; index < AllowedOrigins.Count; index++)
        {
            var origin = AllowedOrigins[index];
            if (!IsOrigin(origin))
            {
                throw new FluentCmsConfigurationException(
                    HostErrorCodes.InvalidOption,
                    $"{key}:AllowedOrigins:{index} is '{origin}'; it must be an http or https origin such as https://app.example.com, and a wildcard is not allowed.");
            }
        }
    }

    private static bool IsOrigin(string? origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && !origin.Contains('*', StringComparison.Ordinal)
        && uri.AbsolutePath == "/" && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
        && string.IsNullOrEmpty(uri.UserInfo)
        && !origin.EndsWith('/');
}
