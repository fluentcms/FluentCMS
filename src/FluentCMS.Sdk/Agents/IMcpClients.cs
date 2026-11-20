namespace FluentCMS.Agents;

/// <summary>
/// The MCP clients of the instance: the OAuth clients that registered themselves at the authorization server (RFC 7591),
/// and those an administrator registered under the same rules. Core implements it over its client store, and the MCP plugin
/// calls it from its Management endpoints and its jobs. It checks no permission, so <c>mcp.manage</c> is enforced by the
/// caller. A client belongs to no site.
/// </summary>
public interface IMcpClients
{
    /// <summary>Lists the MCP clients, the newest registration first.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The clients, with the time of the newest token issued to each.</returns>
    Task<IReadOnlyList<McpClient>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Approves a pending client and gives it a role. A call through the client is then allowed what both the user's roles
    /// and this role allow. Raises <see cref="Events.McpClientApprovedEvent"/>.
    /// </summary>
    /// <param name="id">The id of the client.</param>
    /// <param name="role">The id of a role, or the exact name of an instance-wide role.</param>
    /// <param name="cancellationToken">Cancels the change.</param>
    /// <returns>The outcome, with the client as it is now.</returns>
    Task<McpClientChange> ApproveAsync(Guid id, string role, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a client for an agent that does not register itself, under the rules of dynamic registration: public, with
    /// no secret, the code flow with PKCE and refresh tokens, the user's consent and redirect URIs on https or on loopback.
    /// It is approved at once with the role. Raises <see cref="Events.McpClientRegisteredEvent"/>.
    /// </summary>
    /// <param name="registration">The name, the redirect URIs and the role of the client.</param>
    /// <param name="cancellationToken">Cancels the change.</param>
    /// <returns>The outcome, with the client as it is now.</returns>
    Task<McpClientChange> RegisterAsync(McpClientRegistration registration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes the role of an approved client and ends its tokens, access and refresh tokens alike, and the users' consents
    /// to it, so that its next token comes from a new sign-in, where the user consents again, and carries the new role.
    /// Setting the role it has ends nothing and raises nothing. Either way a client the open policy approved then names the
    /// caller as the one who approved it, so that neither <see cref="RemoveUnusedAsync"/> nor
    /// <see cref="RemoveOpenApprovedAsync"/> removes it. Raises <see cref="Events.McpClientRoleChangedEvent"/>.
    /// </summary>
    /// <param name="id">The id of the client.</param>
    /// <param name="role">The id of a role, or the exact name of an instance-wide role.</param>
    /// <param name="cancellationToken">Cancels the change.</param>
    /// <returns>The outcome, with the client as it is now.</returns>
    Task<McpClientChange> ChangeRoleAsync(Guid id, string role, CancellationToken cancellationToken = default);

    /// <summary>Rejects a pending client, which is then refused for good. Raises <see cref="Events.McpClientRejectedEvent"/>.</summary>
    /// <param name="id">The id of the client.</param>
    /// <param name="cancellationToken">Cancels the change.</param>
    /// <returns>The outcome, with the client as it is now.</returns>
    Task<McpClientChange> RejectAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a client, whatever its status, with its sessions and tokens, so that its access tokens stop at once.
    /// Raises <see cref="Events.McpClientRevokedEvent"/>.
    /// </summary>
    /// <param name="id">The id of the client.</param>
    /// <param name="cancellationToken">Cancels the change.</param>
    /// <returns>The outcome, with the client as it was.</returns>
    Task<McpClientChange> RevokeAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Removes the clients that are not approved, pending or rejected, and registered before a time. It raises no event.</summary>
    /// <param name="registeredBefore">The time before which a client that is still not approved is removed.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The number of clients removed.</returns>
    Task<int> RemoveUnapprovedAsync(DateTimeOffset registeredBefore, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the clients the open policy approved, which no administrator looked at or changed, that have no token or
    /// authorization left: one no user ever consented to when it registered before one time, and any other when it
    /// registered before another. It raises no event.
    /// </summary>
    /// <param name="neverUsedBefore">The time before which a client no user ever consented to is removed.</param>
    /// <param name="unusedBefore">The time before which a client whose tokens and authorizations are all gone is removed.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The number of clients removed.</returns>
    Task<int> RemoveUnusedAsync(DateTimeOffset neverUsedBefore, DateTimeOffset unusedBefore, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the clients the open policy approved that registered in a span of time, used or not, each with its sessions
    /// and tokens as <see cref="RevokeAsync"/> removes one. A client an administrator approved, registered or gave a role
    /// is never removed, nor is a pending or rejected one. Raises <see cref="Events.McpClientRevokedEvent"/> for each.
    /// </summary>
    /// <param name="registeredFrom">The time from which a client registered, or <see langword="null"/> for no start.</param>
    /// <param name="registeredBefore">The time before which a client registered, or <see langword="null"/> for no end.</param>
    /// <param name="cancellationToken">Cancels the work; the clients removed before it stay removed.</param>
    /// <returns>The number of clients removed.</returns>
    Task<int> RemoveOpenApprovedAsync(DateTimeOffset? registeredFrom, DateTimeOffset? registeredBefore, CancellationToken cancellationToken = default);
}

/// <summary>A client an administrator registers.</summary>
/// <param name="Name">The name the consent page shows; <c>MCP client</c> when it is <see langword="null"/>.</param>
/// <param name="RedirectUris">The redirect URIs, as a client that registers itself gives them.</param>
/// <param name="Role">The id of a role, or the exact name of an instance-wide role.</param>
public sealed record McpClientRegistration(string? Name, IReadOnlyList<string> RedirectUris, string Role);

/// <summary>An MCP client that registered itself, or that an administrator registered.</summary>
/// <param name="Id">The id of the client in the store, which the Management API names it by.</param>
/// <param name="ClientId">The OAuth client id it was given.</param>
/// <param name="Name">The name it gave itself, which the consent page shows.</param>
/// <param name="Status">Whether it waits for approval, is approved or was rejected.</param>
/// <param name="RedirectUris">The redirect URIs it registered.</param>
/// <param name="RegisteredAt">When it registered.</param>
public sealed record McpClient(Guid Id, string ClientId, string Name, McpClientStatus Status, IReadOnlyList<string> RedirectUris, DateTimeOffset RegisteredAt)
{
    /// <summary>The role that limits its tokens; <see langword="null"/> until it is approved.</summary>
    public Guid? RoleId { get; init; }

    /// <summary>The name of that role; <see langword="null"/> when it has none or the role is gone, which leaves the client nothing.</summary>
    public string? RoleName { get; init; }

    /// <summary>The scopes it may ask for.</summary>
    public IReadOnlyList<string> Scopes { get; init; } = [];

    /// <summary>The principal that approved it; <see langword="null"/> while it is not approved, or when the open policy approved it.</summary>
    public Guid? ApprovedBy { get; init; }

    /// <summary>When it was approved.</summary>
    public DateTimeOffset? ApprovedAt { get; init; }

    /// <summary>When the newest token was issued to it; <see langword="null"/> when it holds none.</summary>
    public DateTimeOffset? LastActivityAt { get; init; }
}

/// <summary>Where an MCP client stands.</summary>
public enum McpClientStatus
{
    /// <summary>It waits for an administrator, and gets no token.</summary>
    Pending = 0,

    /// <summary>It has a role, and users can sign in through it.</summary>
    Approved = 1,

    /// <summary>An administrator refused it.</summary>
    Rejected = 2,
}

/// <summary>What a change to an MCP client came to.</summary>
/// <param name="Outcome">Whether the change was made, and why not otherwise.</param>
/// <param name="Client">The client, or <see langword="null"/> when there is none of that id.</param>
public sealed record McpClientChange(McpClientChangeOutcome Outcome, McpClient? Client)
{
    /// <summary>What is wrong with a registration that was refused; <see langword="null"/> for any other outcome.</summary>
    public string? Error { get; init; }
}

/// <summary>The outcome of a change to an MCP client.</summary>
public enum McpClientChangeOutcome
{
    /// <summary>The change was made.</summary>
    Done = 0,

    /// <summary>No MCP client has the id.</summary>
    NotFound = 1,

    /// <summary>The client is not pending, so it can be neither approved nor rejected.</summary>
    NotPending = 2,

    /// <summary>No role has the id or the name given.</summary>
    UnknownRole = 3,

    /// <summary>The client is not approved, so its role is not changed: a pending client is approved with a role.</summary>
    NotApproved = 4,

    /// <summary>The name or the redirect URIs of a registration were refused, as <see cref="McpClientChange.Error"/> says.</summary>
    InvalidRegistration = 5,

    /// <summary>Another change of the client was made at the same time, so this one was not.</summary>
    ChangedMeanwhile = 6,
}
