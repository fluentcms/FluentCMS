using FluentCMS.Core.DependencyInjection;
using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Core.Identity;

// cms.Users: the password policy, lockout and invitations of admin users. Identity's defaults, except a longer password.
public sealed class AdminUserOptions
{
    public PasswordOptions Password { get; } = new() { RequiredLength = 12 };

    public LockoutOptions Lockout { get; } = new();

    public TimeSpan InvitationLifetime { get; set; } = TimeSpan.FromDays(7);

    internal void Validate()
    {
        if (Password.RequiredLength < 1 || Password.RequiredUniqueChars < 0)
        {
            throw new FluentCmsConfigurationException(IdentityErrorCodes.InvalidOptions, "cms.Users.Password.RequiredLength must be at least 1, and RequiredUniqueChars not negative.");
        }

        if (Lockout.MaxFailedAccessAttempts < 1 || Lockout.DefaultLockoutTimeSpan <= TimeSpan.Zero)
        {
            throw new FluentCmsConfigurationException(IdentityErrorCodes.InvalidOptions, "cms.Users.Lockout.MaxFailedAccessAttempts must be at least 1, and DefaultLockoutTimeSpan positive.");
        }

        if (InvitationLifetime <= TimeSpan.Zero)
        {
            throw new FluentCmsConfigurationException(IdentityErrorCodes.InvalidOptions, "cms.Users.InvitationLifetime must be positive.");
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
