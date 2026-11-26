using FluentCMS.Core.DependencyInjection;

namespace FluentCMS.Core.Identity.Oidc;

// A certificate the configuration gives the authorization server, as 'fluentcms keys list' and the startup report name it.
internal sealed record ConfiguredCertificate(string Thumbprint, bool Signs, DateTimeOffset NotBefore, DateTimeOffset NotAfter);

internal static class SigningCertificates
{
    public const string Key = $"{FluentCmsOptions.Section}:{SecurityOptions.Section}:{nameof(SecurityOptions.SigningCertificate)}";

    public static IReadOnlyList<ConfiguredCertificate> Of(SecurityOptions security)
    {
        List<ConfiguredCertificate> certificates = [];
        if (security.SigningCertificate.IsConfigured)
        {
            using var certificate = security.SigningCertificate.Load();
            certificates.Add(new ConfiguredCertificate(certificate.Thumbprint, Signs: true, certificate.NotBefore.ToUniversalTime(), certificate.NotAfter.ToUniversalTime()));
        }

        return certificates;
    }
}
