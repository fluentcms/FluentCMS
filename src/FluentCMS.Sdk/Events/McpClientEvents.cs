namespace FluentCMS.Events;

/// <summary>An MCP client registered itself at the authorization server.</summary>
/// <param name="Id">The id of the client in the store.</param>
/// <param name="ClientId">The OAuth client id it was given.</param>
/// <param name="Name">The name it gave itself.</param>
/// <param name="Status">Its status after the registration: <c>pending</c>, or <c>approved</c> under the open policy.</param>
/// <param name="RoleId">The role the open policy gave it; <see langword="null"/> for a pending client.</param>
/// <param name="Ip">The client address of the registration, as the host trusts it.</param>
public sealed record McpClientRegisteredEvent(Guid Id, string ClientId, string Name, string Status, Guid? RoleId, string? Ip) : CmsEvent;

/// <summary>An administrator approved a pending MCP client and gave it a role.</summary>
/// <param name="Id">The id of the client in the store.</param>
/// <param name="ClientId">Its OAuth client id.</param>
/// <param name="Name">Its name.</param>
/// <param name="RoleId">The role that limits its tokens.</param>
/// <param name="RoleName">The name of that role.</param>
public sealed record McpClientApprovedEvent(Guid Id, string ClientId, string Name, Guid RoleId, string RoleName) : CmsEvent;

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
