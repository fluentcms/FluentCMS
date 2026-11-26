using System.Security.Cryptography.X509Certificates;
using FluentCMS.Core.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FluentCMS.Core.Identity.Oidc;

// A certificate the configuration gives the authorization server, as 'fluentcms keys list' and the startup report name it.
internal sealed record ConfiguredCertificate(string Thumbprint, bool Signs, DateTimeOffset NotBefore, DateTimeOffset NotAfter);

// The certificates of FluentCms:Security, loaded once: the options of OpenIddict are built many times, and each load would
// open the files again and leave certificates behind. SigningCertificate signs. PreviousSigningCertificates are accepted and
// listed in the key set and never sign, so they are kept without their private keys: the previous certificate for the
// overlap after it was replaced, and the next one for the lead before it becomes the signing certificate (ADR 0008).
internal sealed class SigningCertificates(SecurityOptions security) : IDisposable
{
    public const string Key = $"{FluentCmsOptions.Section}:{SecurityOptions.Section}:{nameof(SecurityOptions.SigningCertificate)}";

    public const string PreviousKey = $"{FluentCmsOptions.Section}:{SecurityOptions.Section}:{nameof(SecurityOptions.PreviousSigningCertificates)}";

    private readonly Lazy<X509Certificate2?> _signing = new(() => security.SigningCertificate.IsConfigured ? security.SigningCertificate.Load() : null);
    private readonly Lazy<IReadOnlyList<X509Certificate2>> _validating = new(() => LoadValidating(security));
    private X509SecurityKey? _signingKey;
    private IReadOnlyList<X509SecurityKey>? _validationKeys;

    // The certificate that signs, with its private key; null while a generated key signs.
    public X509SecurityKey? SigningKey => _signingKey ??= _signing.Value is { } certificate ? new X509SecurityKey(certificate) : null;

    // One key per certificate that only validates. One listed twice, or listed while it is also the signing certificate,
    // counts once, so no kid is in the key set twice.
    public IReadOnlyList<X509SecurityKey> ValidationKeys =>
        _validationKeys ??= [.. _validating.Value.Where(certificate => !Signs(certificate)).Select(certificate => new X509SecurityKey(certificate))];

    public IReadOnlyList<ConfiguredCertificate> Describe()
    {
        List<ConfiguredCertificate> certificates = [];
        if (_signing.Value is { } signing)
        {
            certificates.Add(Describe(signing, signs: true));
        }

        certificates.AddRange(_validating.Value.Where(certificate => !Signs(certificate)).Select(certificate => Describe(certificate, signs: false)));
        return certificates;
    }

    public void Dispose()
    {
        if (_signing.IsValueCreated)
        {
            _signing.Value?.Dispose();
        }

        if (_validating.IsValueCreated)
        {
            foreach (var certificate in _validating.Value)
            {
                certificate.Dispose();
            }
        }
    }

    private bool Signs(X509Certificate2 certificate) =>
        _signing.Value is { } signing && string.Equals(signing.Thumbprint, certificate.Thumbprint, StringComparison.OrdinalIgnoreCase);

    private static ConfiguredCertificate Describe(X509Certificate2 certificate, bool signs) =>
        new(certificate.Thumbprint, signs, certificate.NotBefore.ToUniversalTime(), certificate.NotAfter.ToUniversalTime());

    private static List<X509Certificate2> LoadValidating(SecurityOptions security)
    {
        List<X509Certificate2> certificates = [];
        foreach (var (entry, index) in security.ConfiguredPreviousSigningCertificates)
        {
            using var loaded = entry.Load($"{nameof(SecurityOptions.PreviousSigningCertificates)}:{index}", privateKey: false);
            if (!certificates.Exists(certificate => string.Equals(certificate.Thumbprint, loaded.Thumbprint, StringComparison.OrdinalIgnoreCase)))
            {
                certificates.Add(X509CertificateLoader.LoadCertificate(loaded.RawData));
            }
        }

        return certificates;
    }
}

// A signing certificate outside its validity period stops the host: FluentCMS, and no longer the order OpenIddict gives
// the certificates, decides that it signs, and OpenIddict does not build its options when no certificate it has is valid.
internal sealed class SigningCertificateValidity(TimeProvider time) : IValidateOptions<SecurityOptions>
{
    public ValidateOptionsResult Validate(string? name, SecurityOptions options)
    {
        if (name != Options.DefaultName || !options.SigningCertificate.IsConfigured)
        {
            return ValidateOptionsResult.Success;
        }

        using var certificate = options.SigningCertificate.Load();
        var now = time.GetUtcNow();
        if (certificate.NotBefore.ToUniversalTime() > now || certificate.NotAfter.ToUniversalTime() < now)
        {
            throw new FluentCmsConfigurationException(
                OidcErrorCodes.SigningCertificateUnusable,
                $"{SigningCertificates.Key}:{nameof(SigningCertificateOptions.Path)} is '{options.SigningCertificate.Path}', a certificate (thumbprint {certificate.Thumbprint}) that is valid from {certificate.NotBefore.ToUniversalTime():u} to {certificate.NotAfter.ToUniversalTime():u} only, so it cannot sign now. Set the certificate that signs there; one that is not yet valid, or one that has expired and whose tokens are still good, belongs under {SigningCertificates.PreviousKey}.");
        }

        return ValidateOptionsResult.Success;
    }
}
