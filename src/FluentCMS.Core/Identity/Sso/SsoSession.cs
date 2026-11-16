using Microsoft.AspNetCore.Authentication;

namespace FluentCMS.Core.Identity.Sso;

// The provider's session of a sign-in through a provider of cms.Sso: the provider's name, and the sid and iss of its
// validated id token. The handler writes it to the external cookie's properties, and the sign-in copies it to the sign-in
// cookie's, which its renewals keep, so that the provider's front-channel logout can name the sign-in it ends (#3204).
internal sealed record SsoSession(string Provider, string Sid, string Issuer)
{
    private const string ProviderItem = ".fcms.sso_provider";
    private const string SidItem = ".fcms.sso_sid";
    private const string IssuerItem = ".fcms.sso_iss";

    public static SsoSession? Of(AuthenticationProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        return properties.Items.TryGetValue(ProviderItem, out var provider) && !string.IsNullOrEmpty(provider)
            && properties.Items.TryGetValue(SidItem, out var sid) && !string.IsNullOrEmpty(sid)
            && properties.Items.TryGetValue(IssuerItem, out var issuer) && !string.IsNullOrEmpty(issuer)
                ? new SsoSession(provider, sid, issuer)
                : null;
    }

    public void WriteTo(AuthenticationProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        properties.Items[ProviderItem] = Provider;
        properties.Items[SidItem] = Sid;
        properties.Items[IssuerItem] = Issuer;
    }

    // Both values of the request must be sent and be the session's, so one left out matches nothing.
    public bool IsNamedBy(string provider, string? sid, string? issuer) =>
        string.Equals(Provider, provider, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Sid, sid, StringComparison.Ordinal)
        && string.Equals(Issuer, issuer, StringComparison.Ordinal);
}
