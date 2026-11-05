namespace FluentCMS.Core.DependencyInjection;

// FluentCms:Security.
public sealed class SecurityOptions
{
    public const string Section = "Security";

    // Lets Production start with the signing key the authorization server generates when no certificate is configured.
    public bool AllowGeneratedSigningKey { get; set; }

    public SecurityCorsOptions Cors { get; } = new();

    internal void Validate()
    {
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
