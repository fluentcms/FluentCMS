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

    // Certificates whose tokens are accepted and that are listed in the key set, and that never sign: the one a new
    // SigningCertificate replaced, until its tokens have expired, and the next one, before it becomes the SigningCertificate.
    public IList<SigningCertificateOptions> PreviousSigningCertificates { get; } = [];

    // When the keys the authorization server generates are replaced, and how long a key is published before it signs and
    // accepted after it stopped.
    public KeyRotationOptions KeyRotation { get; } = new();

    public SecurityCorsOptions Cors { get; } = new();

    // The Referrer-Policy header of FluentCMS responses.
    public string ReferrerPolicy { get; set; } = "no-referrer";

    // The sources that may frame the hosted pages, as the frame-ancestors directive takes them. None by default.
    public string FrameAncestors { get; set; } = "'none'";

    // An entry whose Path and Password are both empty is absent, as an empty SigningCertificate:Path is, so that the
    // environment can turn off an entry of a settings file, which it cannot remove.
    internal IEnumerable<(SigningCertificateOptions Entry, int Index)> ConfiguredPreviousSigningCertificates =>
        PreviousSigningCertificates.Select((entry, index) => (entry, index)).Where(item => item.entry.IsConfigured || !string.IsNullOrEmpty(item.entry.Password));

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
        foreach (var (entry, index) in ConfiguredPreviousSigningCertificates)
        {
            entry.Load($"{nameof(PreviousSigningCertificates)}:{index}", privateKey: false).Dispose();
        }

        KeyRotation.Validate();
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

    // Windows otherwise writes the private key to the user's key folder until the certificate is disposed, and leaves it
    // there when the process is killed. Linux writes nothing either way, and macOS does not support an ephemeral key.
    internal static X509KeyStorageFlags KeyStorage => OperatingSystem.IsWindows() ? X509KeyStorageFlags.EphemeralKeySet : X509KeyStorageFlags.DefaultKeySet;

    // key is where the configuration has the entry, for the message. A certificate that only validates needs no private key.
    internal X509Certificate2 Load(string key = nameof(SecurityOptions.SigningCertificate), bool privateKey = true)
    {
        if (!IsConfigured)
        {
            throw new FluentCmsConfigurationException(
                OidcErrorCodes.SigningCertificateUnusable,
                $"{key}:{nameof(Path)} is not set; each entry names its PKCS#12 file.");
        }

        X509Certificate2 certificate;
        try
        {
            certificate = X509CertificateLoader.LoadPkcs12FromFile(Path!, Password, KeyStorage);
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or UnauthorizedAccessException)
        {
            throw new FluentCmsConfigurationException(
                OidcErrorCodes.SigningCertificateUnusable,
                $"{key}:{nameof(Path)} is '{Path}', which cannot be read as a PKCS#12 file with this password: {exception.Message}");
        }

        using var rsa = privateKey ? certificate.GetRSAPrivateKey() : certificate.GetRSAPublicKey();
        if (rsa is null)
        {
            certificate.Dispose();
            throw new FluentCmsConfigurationException(
                OidcErrorCodes.SigningCertificateUnusable,
                privateKey
                    ? $"{key}:{nameof(Path)} is '{Path}', which holds no RSA private key; the authorization server signs with RS256."
                    : $"{key}:{nameof(Path)} is '{Path}', which holds no RSA key; the authorization server signs with RS256.");
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

// FluentCms:Security:KeyRotation (ADR 0008). The keys in cms_signing_key are replaced by 'fluentcms keys rotate' and, when
// Enabled, by the job oidc.keys; PublishLead and Overlap hold for both.
public sealed class KeyRotationOptions
{
    internal const string Section = "KeyRotation";

    // Every instance reads the keys within 30 seconds, so each has a new key before any signs with it.
    internal static readonly TimeSpan MinimumPublishLead = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan Maximum = TimeSpan.FromDays(3650);

    // Turns the routine rotation on. Off by default: a validator outside the host that pinned a key breaks at the first one.
    public bool Enabled { get; set; }

    // How long a key signs or encrypts before its successor does.
    public TimeSpan Interval { get; set; } = TimeSpan.FromDays(90);

    // How long a new key is accepted, and a signing key listed in the key set, before it signs or encrypts.
    public TimeSpan PublishLead { get; set; } = TimeSpan.FromDays(1);

    // How long a key is still accepted after its successor signs. Unset, it is the longest lifetime of a token, the 30
    // seconds in which a used refresh token is taken again and 5 minutes for clocks; a shorter one stops the host.
    public TimeSpan? Overlap { get; set; }

    internal void Validate()
    {
        if (PublishLead < MinimumPublishLead || PublishLead > Maximum)
        {
            throw new FluentCmsConfigurationException(
                HostErrorCodes.InvalidOption,
                $"{Section}:{nameof(PublishLead)} is {PublishLead}; it must be from {MinimumPublishLead} to {Maximum.TotalDays:0} days, so that every instance has read a new key before one signs with it.");
        }

        if (Interval <= PublishLead || Interval > Maximum)
        {
            throw new FluentCmsConfigurationException(
                HostErrorCodes.InvalidOption,
                $"{Section}:{nameof(Interval)} is {Interval}; it must be more than {nameof(PublishLead)} ({PublishLead}) and at most {Maximum.TotalDays:0} days.");
        }

        if (Overlap is { } overlap && (overlap <= TimeSpan.Zero || overlap > Maximum))
        {
            throw new FluentCmsConfigurationException(
                HostErrorCodes.InvalidOption,
                $"{Section}:{nameof(Overlap)} is {overlap}; it must be more than zero and at most {Maximum.TotalDays:0} days, or left unset for the longest lifetime of a token.");
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
