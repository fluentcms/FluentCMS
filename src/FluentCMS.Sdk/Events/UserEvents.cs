namespace FluentCMS.Events;

/// <summary>An admin user was created.</summary>
/// <param name="UserId">The id of the user.</param>
/// <param name="UserName">Its user name.</param>
public sealed record UserCreatedEvent(Guid UserId, string UserName) : CmsEvent;

/// <summary>An admin user was changed, such as its email, its password or whether it must change its password.</summary>
/// <param name="UserId">The id of the user.</param>
/// <param name="UserName">Its user name after the change.</param>
/// <param name="Changes">What changed, by name and never by value, such as <c>email</c> or <c>password</c>.</param>
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
