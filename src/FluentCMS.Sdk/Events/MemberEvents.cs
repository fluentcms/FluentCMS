namespace FluentCMS.Events;

/// <summary>A member was added to the member pool, by registering or by an administrator. Its address is not verified yet.</summary>
/// <param name="MemberId">The id of the member.</param>
/// <param name="Email">Its email address, which is its sign-in name.</param>
public sealed record MemberRegisteredEvent(Guid MemberId, string Email) : CmsEvent;

/// <summary>A member's email address was verified, with the link of the verification mail or by setting a password through a mailed link.</summary>
/// <param name="MemberId">The id of the member.</param>
/// <param name="Email">Its email address.</param>
public sealed record MemberVerifiedEvent(Guid MemberId, string Email) : CmsEvent;

/// <summary>A member was changed, such as its display name or its password.</summary>
/// <param name="MemberId">The id of the member.</param>
/// <param name="Email">Its email address.</param>
/// <param name="Changes">What changed, by name and never by value: the names of <see cref="MemberChanges"/>.</param>
public sealed record MemberUpdatedEvent(Guid MemberId, string Email, IReadOnlyList<string> Changes) : CmsEvent;

/// <summary>A member was deactivated. It cannot sign in, and its tokens are refused, until it is reactivated.</summary>
/// <param name="MemberId">The id of the member.</param>
/// <param name="Email">Its email address.</param>
public sealed record MemberDeactivatedEvent(Guid MemberId, string Email) : CmsEvent;

/// <summary>A member was deleted, with its logins, tokens and group memberships. Activity rows keep its id.</summary>
/// <param name="MemberId">The id the member had.</param>
/// <param name="Email">The email address it had.</param>
public sealed record MemberDeletedEvent(Guid MemberId, string Email) : CmsEvent;

/// <summary>A member was put in member groups or taken out of them.</summary>
/// <param name="MemberId">The id of the member.</param>
/// <param name="Email">Its email address.</param>
/// <param name="Added">The ids of the groups it was put in.</param>
/// <param name="Removed">The ids of the groups it was taken out of.</param>
public sealed record MemberGroupChangedEvent(Guid MemberId, string Email, IReadOnlyList<Guid> Added, IReadOnlyList<Guid> Removed) : CmsEvent;

/// <summary>A member group was created.</summary>
/// <param name="GroupId">The id of the group.</param>
/// <param name="Name">Its name.</param>
/// <param name="RoleId">The id of the role its members hold.</param>
public sealed record MemberGroupCreatedEvent(Guid GroupId, string Name, Guid RoleId) : CmsEvent;

/// <summary>A member group was changed: its name, its description or the role its members hold.</summary>
/// <param name="GroupId">The id of the group.</param>
/// <param name="Name">Its name after the change.</param>
/// <param name="RoleId">The id of the role its members hold after the change.</param>
public sealed record MemberGroupUpdatedEvent(Guid GroupId, string Name, Guid RoleId) : CmsEvent;

/// <summary>A member group was deleted. Its members are members still, without what the group gave them.</summary>
/// <param name="GroupId">The id the group had.</param>
/// <param name="Name">The name it had.</param>
public sealed record MemberGroupDeletedEvent(Guid GroupId, string Name) : CmsEvent;

/// <summary>A member signed in, on the hosted pages or with the direct login grant.</summary>
/// <param name="MemberId">The id of the member.</param>
/// <param name="Email">Its email address.</param>
/// <param name="Method">How it signed in: one of <see cref="MemberLoginMethods"/>, or the name of an external provider.</param>
/// <param name="Ip">The client address of the request, when it is known.</param>
public sealed record MemberLoginSucceededEvent(Guid MemberId, string Email, string Method, string? Ip) : CmsEvent;

/// <summary>A sign-in of a member was refused. It never carries what was typed: an address that matched no member is not kept.</summary>
/// <param name="MemberId">The id of the member the sign-in was for, or <see langword="null"/> when no member matched.</param>
/// <param name="Email">That member's email address, or <see langword="null"/> when no member matched.</param>
/// <param name="Method">How it tried to sign in: one of <see cref="MemberLoginMethods"/>, or the name of an external provider.</param>
/// <param name="Reason">Why it was refused: one of <see cref="LoginFailureReasons"/>.</param>
/// <param name="Ip">The client address of the request, when it is known.</param>
public sealed record MemberLoginFailedEvent(Guid? MemberId, string? Email, string Method, string Reason, string? Ip) : CmsEvent;

/// <summary>The names <see cref="MemberUpdatedEvent.Changes"/> uses for what changed on a member.</summary>
public static class MemberChanges
{
    /// <summary>The display name.</summary>
    public const string DisplayName = "displayName";

    /// <summary>The password was set, changed or reset. A rehash of the same password at sign-in is not a change.</summary>
    public const string Password = "password";

    /// <summary>A deactivated member was reactivated. Deactivation is <see cref="MemberDeactivatedEvent"/>.</summary>
    public const string Reactivated = "reactivated";
}

/// <summary>The names <see cref="MemberLoginSucceededEvent.Method"/> and <see cref="MemberLoginFailedEvent.Method"/> use for a sign-in that no external provider made.</summary>
public static class MemberLoginMethods
{
    /// <summary>The email address and the password, on the hosted login page.</summary>
    public const string Password = "password";

    /// <summary>The email address and the password, sent to the token endpoint with the direct login grant.</summary>
    public const string DirectLogin = "direct_login";
}
