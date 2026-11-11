using FluentCMS.Core.DependencyInjection;
using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Core.Identity.Members;

// cms.Members, and FluentCms:Members in configuration: the member pool. Registration and direct login are off until a
// host turns them on. The password policy and the lockout are the pool's own, apart from those of the admin users.
public sealed class MemberOptions
{
    internal const string Section = "Members";

    // Lets a visitor register on /api/members/v1/register and on the hosted page. Off, both answer 404, and members
    // come from the Management API only.
    public bool AllowRegistration { get; set; }

    // Lets a client that also sets AllowDirectLogin send a member's address and password to the token endpoint
    // (grant_type=member_password). It is the less secure way in: the app sees the password, and the hosted pages do not.
    public bool AllowDirectLogin { get; set; }

    public PasswordOptions Password { get; } = new() { RequiredLength = 12 };

    // A member's lockout is always on, so AllowedForNewUsers is not read.
    public LockoutOptions Lockout { get; } = new();

    // How long the link of a verification mail works.
    public TimeSpan EmailVerificationLifetime { get; set; } = TimeSpan.FromHours(24);

    // How long the link that sets or resets a password works.
    public TimeSpan PasswordResetLifetime { get; set; } = TimeSpan.FromHours(1);

    // A member that has not verified its address this long after it was created is removed.
    public TimeSpan UnverifiedMemberLifetime { get; set; } = TimeSpan.FromDays(7);

    public MemberLinkOptions Links { get; } = new();

    // The external providers members sign in with, by name. They are apart from the providers of cms.Sso, which sign in
    // admin users only.
    public IDictionary<string, MemberProvider> Providers { get; } = new Dictionary<string, MemberProvider>(StringComparer.OrdinalIgnoreCase);

    internal void Validate()
    {
        Require(Password.RequiredLength >= 1, $"{nameof(Password)}:{nameof(Password.RequiredLength)} is {Password.RequiredLength}; it must be at least 1.");
        Require(Password.RequiredUniqueChars >= 0, $"{nameof(Password)}:{nameof(Password.RequiredUniqueChars)} is {Password.RequiredUniqueChars}; it must not be negative.");
        Require(Lockout.MaxFailedAccessAttempts >= 1, $"{nameof(Lockout)}:{nameof(Lockout.MaxFailedAccessAttempts)} is {Lockout.MaxFailedAccessAttempts}; it must be at least 1.");
        Require(Lockout.DefaultLockoutTimeSpan > TimeSpan.Zero, $"{nameof(Lockout)}:{nameof(Lockout.DefaultLockoutTimeSpan)} must be positive.");
        Require(EmailVerificationLifetime > TimeSpan.Zero, $"{nameof(EmailVerificationLifetime)} must be positive.");
        Require(PasswordResetLifetime > TimeSpan.Zero, $"{nameof(PasswordResetLifetime)} must be positive.");
        Require(UnverifiedMemberLifetime >= EmailVerificationLifetime, $"{nameof(UnverifiedMemberLifetime)} is shorter than {nameof(EmailVerificationLifetime)}, so a member would be removed while its verification link still works.");
        Links.Validate();
        foreach (var (name, provider) in Providers)
        {
            provider.Validate(name);
        }
    }

    internal void Apply(IdentityOptions identity)
    {
        identity.Password.RequiredLength = Password.RequiredLength;
        identity.Password.RequiredUniqueChars = Password.RequiredUniqueChars;
        identity.Password.RequireDigit = Password.RequireDigit;
        identity.Password.RequireLowercase = Password.RequireLowercase;
        identity.Password.RequireUppercase = Password.RequireUppercase;
        identity.Password.RequireNonAlphanumeric = Password.RequireNonAlphanumeric;

        identity.Lockout.AllowedForNewUsers = true;
        identity.Lockout.MaxFailedAccessAttempts = Lockout.MaxFailedAccessAttempts;
        identity.Lockout.DefaultLockoutTimeSpan = Lockout.DefaultLockoutTimeSpan;

        // An address is one member, and a member signs in only with a verified address.
        identity.User.RequireUniqueEmail = true;
        identity.SignIn.RequireConfirmedEmail = true;
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new FluentCmsConfigurationException(MemberErrorCodes.InvalidOptions, message);
        }
    }
}

// FluentCms:Members:Links: where the links of member mail go. A template is a URL of the front end with {email} and
// {token} in it, such as https://app.example.com/verify?email={email}&token={token}. Left unset, the link goes to the
// hosted page on the public origin. No link is ever built from a request.
public sealed class MemberLinkOptions
{
    public const string EmailPlaceholder = "{email}";
    public const string TokenPlaceholder = "{token}";

    public string? VerifyEmail { get; set; }

    public string? ResetPassword { get; set; }

    internal void Validate()
    {
        Check(VerifyEmail, nameof(VerifyEmail));
        Check(ResetPassword, nameof(ResetPassword));
    }

    // The placeholders are after the host, so that neither value can name where the link goes.
    private static void Check(string? template, string name)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return;
        }

        var key = $"{nameof(MemberOptions.Links)}:{name}";
        var valid = Uri.TryCreate(template.Replace(EmailPlaceholder, "e", StringComparison.Ordinal).Replace(TokenPlaceholder, "t", StringComparison.Ordinal), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            && uri.UserInfo.Length == 0;
        MemberOptions.Require(valid, $"{key} is '{template}'; it must be an absolute http or https URL with no user, such as https://app.example.com/verify?email={EmailPlaceholder}&token={TokenPlaceholder}.");

        var hostEnd = template.IndexOfAny(['/', '?', '#'], template.IndexOf("://", StringComparison.Ordinal) + 3);
        foreach (var placeholder in (string[])[EmailPlaceholder, TokenPlaceholder])
        {
            var at = template.IndexOf(placeholder, StringComparison.Ordinal);
            MemberOptions.Require(at >= 0, $"{key} is '{template}'; it has no {placeholder}, which the page that receives the link needs.");
            MemberOptions.Require(hostEnd >= 0 && at > hostEnd, $"{key} is '{template}'; {placeholder} must come after the host, in the path, the query or the fragment.");
        }
    }
}
