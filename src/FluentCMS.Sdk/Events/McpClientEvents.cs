namespace FluentCMS.Events;

/// <summary>An MCP client registered itself at the authorization server, or an administrator registered one.</summary>
/// <param name="Id">The id of the client in the store.</param>
/// <param name="ClientId">The OAuth client id it was given.</param>
/// <param name="Name">The name it was registered with.</param>
/// <param name="Status">
/// Its status after the registration: <c>pending</c>, or <c>approved</c> under the open policy or when an administrator
/// registered it.
/// </param>
/// <param name="RoleId">The role the open policy or the administrator gave it; <see langword="null"/> for a pending client.</param>
/// <param name="Ip">
/// The client address of a client that registered itself, as the host trusts it; <see langword="null"/> when an
/// administrator registered it.
/// </param>
public sealed record McpClientRegisteredEvent(Guid Id, string ClientId, string Name, string Status, Guid? RoleId, string? Ip) : CmsEvent;

/// <summary>An administrator approved a pending MCP client and gave it a role.</summary>
/// <param name="Id">The id of the client in the store.</param>
/// <param name="ClientId">Its OAuth client id.</param>
/// <param name="Name">Its name.</param>
/// <param name="RoleId">The role that limits its tokens.</param>
/// <param name="RoleName">The name of that role.</param>
public sealed record McpClientApprovedEvent(Guid Id, string ClientId, string Name, Guid RoleId, string RoleName) : CmsEvent;

/// <summary>An administrator changed the role of an approved MCP client, which ended its tokens and the users' consents to it.</summary>
/// <param name="Id">The id of the client in the store.</param>
/// <param name="ClientId">Its OAuth client id.</param>
/// <param name="Name">Its name.</param>
/// <param name="RoleId">The role that limits its tokens now.</param>
/// <param name="RoleName">The name of that role.</param>
/// <param name="PreviousRoleId">The role it had before.</param>
public sealed record McpClientRoleChangedEvent(Guid Id, string ClientId, string Name, Guid RoleId, string RoleName, Guid PreviousRoleId) : CmsEvent;

/// <summary>An administrator rejected a pending MCP client.</summary>
/// <param name="Id">The id of the client in the store.</param>
/// <param name="ClientId">Its OAuth client id.</param>
/// <param name="Name">Its name.</param>
public sealed record McpClientRejectedEvent(Guid Id, string ClientId, string Name) : CmsEvent;

/// <summary>An MCP client was removed with its sessions and tokens.</summary>
/// <param name="Id">The id the client had in the store.</param>
/// <param name="ClientId">Its OAuth client id.</param>
/// <param name="Name">Its name.</param>
public sealed record McpClientRevokedEvent(Guid Id, string ClientId, string Name) : CmsEvent;
