namespace FluentCMS.Host.Identity;

// The one external identity provider an owner can configure (USR-01). Off until the authority and client id are set (P10).
public sealed class OpenIdConnectProviderOptions
{
    public const string SectionName = "Authentication:OpenIdConnect";
    public const string Scheme = "oidc";

    public string? Authority { get; set; }

    public string? ClientId { get; set; }

    // From the environment or a secret store, never from a file in the repository.
    public string? ClientSecret { get; set; }

    public string DisplayName { get; set; } = "single sign-on";

    // For providers that never send email_verified (Microsoft Entra). Only safe when the provider issues verified addresses alone,
    // because the first sign-in links to the user with the same email.
    public bool AssumeVerifiedEmail { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Authority) && !string.IsNullOrWhiteSpace(ClientId);

    public string AuthorityOrigin => new Uri(Authority!).GetLeftPart(UriPartial.Authority);
}
