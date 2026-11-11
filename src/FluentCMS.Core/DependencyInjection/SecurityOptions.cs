using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentCMS.Core.Identity.Oidc;

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

    // Lets Production start without FluentCms:PublicOrigin, building links from the request, when a proxy filters the Host.
    public bool AllowRequestOrigin { get; set; }

    // Lets Production start with the Data Protection key ring stored unencrypted, when no XML encryptor is registered.
    public bool AllowUnencryptedKeyRing { get; set; }

    // The certificate the authorization server signs tokens with. Without one it signs with a key it generates and stores.
    public SigningCertificateOptions SigningCertificate { get; } = new();

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

        SigningCertificate.Validate();
        Cors.Delivery.Validate("Cors:Delivery");
        Cors.Management.Validate("Cors:Management");
        Cors.Members.Validate("Cors:Members");
    }
}

// A PKCS#12 (.pfx) file with its private key, and its password, which belongs in a secret store.
public sealed class SigningCertificateOptions
{
    public string? Path { get; set; }

    public string? Password { get; set; }

    internal bool IsConfigured => !string.IsNullOrWhiteSpace(Path);

    internal X509Certificate2 Load()
    {
        X509Certificate2 certificate;
        try
        {
            certificate = X509CertificateLoader.LoadPkcs12FromFile(Path!, Password);
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or UnauthorizedAccessException)
        {
            throw new FluentCmsConfigurationException(
                OidcErrorCodes.SigningCertificateUnusable,
                $"{nameof(SecurityOptions.SigningCertificate)}:{nameof(Path)} is '{Path}', which cannot be read as a PKCS#12 file with this password: {exception.Message}");
        }

        using var rsa = certificate.GetRSAPrivateKey();
        if (rsa is null)
        {
            certificate.Dispose();
            throw new FluentCmsConfigurationException(
                OidcErrorCodes.SigningCertificateUnusable,
                $"{nameof(SecurityOptions.SigningCertificate)}:{nameof(Path)} is '{Path}', which holds no RSA private key; the authorization server signs with RS256.");
        }

        return certificate;
    }

    internal void Validate()
    {
        if (IsConfigured)
        {
            Load().Dispose();
        }
    }
}

public sealed class SecurityCorsOptions
{
    public CorsOriginsOptions Delivery { get; } = new();

    public CorsOriginsOptions Management { get; } = new();

    // The front ends that call /api/members/v1 from a browser; /connect answers them beside the Management origins.
    public CorsOriginsOptions Members { get; } = new();
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
