using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Events;

namespace FluentCMS.Core.Identity.Members;

// The values of MemberProvider.Type.
public static class MemberProviderTypes
{
    public const string Google = "Google";
    public const string Microsoft = "Microsoft";
    public const string GitHub = "GitHub";
    public const string Apple = "Apple";
    public const string Oidc = "Oidc";
}

internal enum MemberProviderKind
{
    Google,
    Microsoft,
    GitHub,
    Apple,
    Oidc,
}

// One entry of cms.Members.Providers, and FluentCms:Members:Providers:{name} in configuration: an external provider
// members sign in with on the member pages. The name is the login provider of its members in cms_member_login and part
// of its callback path, /account/members/sso/{name}. It is lower-case, so that writing it in another case later cannot
// part the members from their logins. Its authentication scheme is member-{name}, so a provider of
// cms.Sso with the same name is another scheme, and neither pool is reached through the other's provider. Secrets come
// from configuration only.
public sealed partial class MemberProvider
{
    internal const string SchemePrefix = "member-";

    // The length of the display name column of cms_member_login, which keeps the name beside each login.
    private const int MaxDisplayNameLength = 128;

    // A sign-in through a provider names the provider as its method, so a provider is not named like another method.
    private static readonly string[] SignInMethods = [MemberLoginMethods.Password, MemberLoginMethods.DirectLogin];

    // One of MemberProviderTypes; the case does not matter.
    public string? Type { get; set; }

    // The text of the button, "Continue with {DisplayName}"; the type's name, or the provider's for generic OIDC.
    public string? DisplayName { get; set; }

    public string? ClientId { get; set; }

    // Required for Google, Microsoft and GitHub, optional for generic OIDC, and not used for Apple, whose secret the
    // handler makes from PrivateKey.
    public string? ClientSecret { get; set; }

    // Asked for after the scopes the type needs for the address.
    public IList<string> Scopes { get; } = [];

    // Generic OIDC: the issuer.
    public string? Authority { get; set; }

    // Generic OIDC: http for the authority, for a provider on the developer's machine.
    public bool RequireHttpsMetadata { get; set; } = true;

    // Apple: the team id, the id of the Sign in with Apple key, and the key itself, the PEM text of its .p8 file.
    public string? TeamId { get; set; }

    public string? KeyId { get; set; }

    public string? PrivateKey { get; set; }

    // A first sign-in whose address belongs to a member with no login of this provider links that member, when the
    // provider says the address is verified. Off, the sign-in is refused, so that a provider cannot take over an
    // account. Not for Microsoft, which never says an address is verified.
    public bool LinkVerifiedEmail { get; set; }

    internal MemberProviderKind Kind => TryKind(Type, out var kind) ? kind : throw new InvalidOperationException($"'{Type}' is not a type of member provider.");

    internal static string SchemeOf(string name) => SchemePrefix + name;

    internal string Name(string name) => DisplayName is { Length: > 0 } display ? display : Kind switch
    {
        MemberProviderKind.Google => "Google",
        MemberProviderKind.Microsoft => "Microsoft",
        MemberProviderKind.GitHub => "GitHub",
        MemberProviderKind.Apple => "Apple",
        _ => name,
    };

    internal void Validate(string name)
    {
        var key = $"{nameof(MemberOptions.Providers)}:{name}";
        Require(NamePattern().IsMatch(name), $"{key}: a provider name is 1 to 64 lower-case letters, digits or '-'. It is part of the callback path, and the logins of its members are kept under it as it is written.");
        Require(
            !SignInMethods.Contains(name, StringComparer.OrdinalIgnoreCase),
            $"{key}: '{name}' is a sign-in method of the member pool, which a sign-in event would not tell from the provider.");
        Require(TryKind(Type, out var kind), $"{key}:{nameof(Type)} is '{Type}'; it is one of {string.Join(", ", Enum.GetNames<MemberProviderKind>())}.");
        Require(!string.IsNullOrWhiteSpace(ClientId), $"{key}:{nameof(ClientId)} is required.");
        Require(DisplayName is null || (DisplayName.Length <= MaxDisplayNameLength && !DisplayName.Any(char.IsControl)), $"{key}:{nameof(DisplayName)} has at most {MaxDisplayNameLength} characters and no control character.");
        Require(Scopes.All(scope => !string.IsNullOrWhiteSpace(scope) && !scope.Any(char.IsWhiteSpace)), $"{key}:{nameof(Scopes)} holds one scope in each entry.");

        switch (kind)
        {
            case MemberProviderKind.Oidc:
                Require(!string.IsNullOrWhiteSpace(Authority), $"{key}:{nameof(Authority)} is required for a generic OIDC provider.");
                var authority = Uri.TryCreate(Authority!.Trim(), UriKind.Absolute, out var parsed) ? parsed : null;
                Require(
                    authority is not null && (authority.Scheme == Uri.UriSchemeHttps || (!RequireHttpsMetadata && authority.Scheme == Uri.UriSchemeHttp)) && string.IsNullOrEmpty(authority.Query) && string.IsNullOrEmpty(authority.Fragment),
                    $"{key}:{nameof(Authority)} is '{Authority}'; it is an https URL with no query, and http needs {nameof(RequireHttpsMetadata)} off.");
                break;
            case MemberProviderKind.Apple:
                Require(!string.IsNullOrWhiteSpace(TeamId), $"{key}:{nameof(TeamId)} is required for Apple.");
                Require(!string.IsNullOrWhiteSpace(KeyId), $"{key}:{nameof(KeyId)} is required for Apple.");
                Require(string.IsNullOrEmpty(ClientSecret), $"{key}:{nameof(ClientSecret)} is not used for Apple; the handler makes the secret from {nameof(PrivateKey)}.");
                Require(IsPrivateKey(PrivateKey), $"{key}:{nameof(PrivateKey)} is required for Apple and is the PEM text of the key's .p8 file, not its path.");
                break;
            default:
                Require(!string.IsNullOrWhiteSpace(ClientSecret), $"{key}:{nameof(ClientSecret)} is required for {kind}.");
                break;
        }

        Require(
            !LinkVerifiedEmail || kind != MemberProviderKind.Microsoft,
            $"{key}:{nameof(LinkVerifiedEmail)} is not offered for Microsoft, which does not say whether an address is verified.");
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new FluentCmsConfigurationException(MemberErrorCodes.InvalidProvider, message);
        }
    }

    private static bool TryKind(string? type, out MemberProviderKind kind)
    {
        foreach (var candidate in Enum.GetValues<MemberProviderKind>())
        {
            if (string.Equals(candidate.ToString(), type?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                kind = candidate;
                return true;
            }
        }

        kind = default;
        return false;
    }

    // The key is tried the way the handler reads it, so a key that is not one stops the host and not the first sign-in.
    private static bool IsPrivateKey(string? pem)
    {
        if (string.IsNullOrWhiteSpace(pem))
        {
            return false;
        }

        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(pem);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException)
        {
            return false;
        }
    }

    [GeneratedRegex("^[a-z0-9-]{1,64}$")]
    private static partial Regex NamePattern();
}
