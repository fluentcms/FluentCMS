namespace FluentCMS.Events;

/// <summary>An admin user was created.</summary>
/// <param name="UserId">The id of the user.</param>
/// <param name="UserName">Its user name.</param>
public sealed record UserCreatedEvent(Guid UserId, string UserName) : CmsEvent;

/// <summary>An admin user was changed, such as its email, its password or whether it must change its password.</summary>
/// <param name="UserId">The id of the user.</param>
/// <param name="UserName">Its user name after the change.</param>
/// <param name="Changes">What changed, by name and never by value: the names of <see cref="UserChanges"/>.</param>
public sealed record UserUpdatedEvent(Guid UserId, string UserName, IReadOnlyList<string> Changes) : CmsEvent;

/// <summary>An admin user was deactivated. It cannot sign in, and holds no roles, until it is reactivated.</summary>
/// <param name="UserId">The id of the user.</param>
/// <param name="UserName">Its user name.</param>
public sealed record UserDeactivatedEvent(Guid UserId, string UserName) : CmsEvent;

/// <summary>Roles were given to or taken from an admin user.</summary>
/// <param name="UserId">The id of the user.</param>
/// <param name="UserName">Its user name.</param>
/// <param name="Added">The ids of the roles it was given.</param>
/// <param name="Removed">The ids of the roles taken from it.</param>
public sealed record UserRoleChangedEvent(Guid UserId, string UserName, IReadOnlyList<Guid> Added, IReadOnlyList<Guid> Removed) : CmsEvent;

/// <summary>The names <see cref="UserUpdatedEvent.Changes"/> uses for what changed on an admin user.</summary>
public static class UserChanges
{
    /// <summary>The user name.</summary>
    public const string UserName = "userName";

    /// <summary>The email address.</summary>
    public const string Email = "email";

    /// <summary>Whether the email address is confirmed.</summary>
    public const string EmailConfirmed = "emailConfirmed";

    /// <summary>The phone number.</summary>
    public const string PhoneNumber = "phoneNumber";

    /// <summary>The password was set, changed, reset or removed. A rehash of the same password at sign-in is not a change.</summary>
    public const string Password = "password";

    /// <summary>Whether two-factor authentication is on.</summary>
    public const string TwoFactor = "twoFactor";

    /// <summary>Whether failed sign-ins can lock the user out.</summary>
    public const string LockoutEnabled = "lockoutEnabled";

    /// <summary>Whether the user must choose a new password at its next sign-in.</summary>
    public const string MustChangePassword = "mustChangePassword";

    /// <summary>A deactivated user was reactivated. Deactivation is <see cref="UserDeactivatedEvent"/>.</summary>
    public const string Reactivated = "reactivated";
}
