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

/// <summary>An external login was linked to an admin user, so the user signs in through that provider.</summary>
/// <param name="UserId">The id of the user.</param>
/// <param name="UserName">Its user name.</param>
/// <param name="LoginProvider">The name of the provider, as <c>cms_user_login</c> keeps it.</param>
public sealed record UserLoginLinkedEvent(Guid UserId, string UserName, string LoginProvider) : CmsEvent;

/// <summary>An external login was removed from an admin user.</summary>
/// <param name="UserId">The id of the user.</param>
/// <param name="UserName">Its user name.</param>
/// <param name="LoginProvider">The name of the provider, as <c>cms_user_login</c> kept it.</param>
public sealed record UserLoginUnlinkedEvent(Guid UserId, string UserName, string LoginProvider) : CmsEvent;

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

/// <summary>An admin user signed in on the hosted pages.</summary>
/// <param name="UserId">The id of the user.</param>
/// <param name="UserName">Its user name.</param>
/// <param name="Method">The step that completed the sign-in: one of <see cref="LoginMethods"/>.</param>
/// <param name="Ip">The client address of the request, when it is known.</param>
public sealed record LoginSucceededEvent(Guid UserId, string UserName, string Method, string? Ip) : CmsEvent;

/// <summary>A sign-in on the hosted pages was refused. It never carries what was typed: a name that matched no user is not kept.</summary>
/// <param name="UserId">The id of the user the sign-in was for, or <see langword="null"/> when no user matched.</param>
/// <param name="UserName">That user's name, or <see langword="null"/> when no user matched.</param>
/// <param name="Method">The step that was refused: one of <see cref="LoginMethods"/>.</param>
/// <param name="Reason">Why it was refused: one of <see cref="LoginFailureReasons"/>.</param>
/// <param name="Ip">The client address of the request, when it is known.</param>
public sealed record LoginFailedEvent(Guid? UserId, string? UserName, string Method, string Reason, string? Ip) : CmsEvent;

/// <summary>The names <see cref="LoginSucceededEvent.Method"/> and <see cref="LoginFailedEvent.Method"/> use for a step of a sign-in.</summary>
public static class LoginMethods
{
    /// <summary>A user name or email address and a password.</summary>
    public const string Password = "password";

    /// <summary>A code of the user's authenticator app, after the password.</summary>
    public const string TwoFactor = "two_factor";

    /// <summary>A recovery code, in place of the authenticator's code.</summary>
    public const string RecoveryCode = "recovery_code";

    /// <summary>An external identity provider.</summary>
    public const string External = "external";
}

/// <summary>The names <see cref="LoginFailedEvent.Reason"/> uses.</summary>
public static class LoginFailureReasons
{
    /// <summary>No user has that name, email address or external login.</summary>
    public const string UnknownUser = "unknown_user";

    /// <summary>The password or code was wrong.</summary>
    public const string InvalidCredentials = "invalid_credentials";

    /// <summary>The user is locked out after too many failures.</summary>
    public const string LockedOut = "locked_out";

    /// <summary>The user may not sign in, such as a deactivated one.</summary>
    public const string NotAllowed = "not_allowed";
}
