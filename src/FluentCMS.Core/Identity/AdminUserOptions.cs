using FluentCMS.Core.DependencyInjection;
using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Core.Identity;

// cms.Users, and FluentCms:Users in configuration: the password policy, lockout and invitations of admin users. Identity's
// defaults, except a longer password. Not FluentCms:Identity, which members (M5) would share.
public sealed class AdminUserOptions
{
    internal const string Section = "Users";

    public PasswordOptions Password { get; } = new() { RequiredLength = 12 };

    public LockoutOptions Lockout { get; } = new();

    public TimeSpan InvitationLifetime { get; set; } = TimeSpan.FromDays(7);

    internal void Validate()
    {
        Require(Password.RequiredLength >= 1, $"{nameof(Password)}:{nameof(Password.RequiredLength)} is {Password.RequiredLength}; it must be at least 1.");
        Require(Password.RequiredUniqueChars >= 0, $"{nameof(Password)}:{nameof(Password.RequiredUniqueChars)} is {Password.RequiredUniqueChars}; it must not be negative.");
        Require(Lockout.MaxFailedAccessAttempts >= 1, $"{nameof(Lockout)}:{nameof(Lockout.MaxFailedAccessAttempts)} is {Lockout.MaxFailedAccessAttempts}; it must be at least 1.");
        Require(Lockout.DefaultLockoutTimeSpan > TimeSpan.Zero, $"{nameof(Lockout)}:{nameof(Lockout.DefaultLockoutTimeSpan)} must be positive.");
        Require(InvitationLifetime > TimeSpan.Zero, $"{nameof(InvitationLifetime)} must be positive.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new FluentCmsConfigurationException(IdentityErrorCodes.InvalidOptions, message);
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

        identity.Lockout.AllowedForNewUsers = Lockout.AllowedForNewUsers;
        identity.Lockout.MaxFailedAccessAttempts = Lockout.MaxFailedAccessAttempts;
        identity.Lockout.DefaultLockoutTimeSpan = Lockout.DefaultLockoutTimeSpan;

        // Invitations go by email, so an address belongs to one user.
        identity.User.RequireUniqueEmail = true;
    }
}
