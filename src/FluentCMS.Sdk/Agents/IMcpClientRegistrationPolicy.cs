namespace FluentCMS.Agents;

/// <summary>
/// Says whether MCP clients may register themselves at the authorization server (RFC 7591), and how. The MCP plugin
/// implements it from its settings, and core asks it on every registration and for the discovery document. A host without
/// an implementation, which is one without the MCP plugin, has no registration endpoint.
/// </summary>
public interface IMcpClientRegistrationPolicy
{
    /// <summary>Gets the policy as it is now.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The policy.</returns>
    ValueTask<McpClientRegistrationPolicy> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>How MCP clients register.</summary>
/// <param name="Mode">Whether registration is off, waits for approval or approves at once.</param>
public sealed record McpClientRegistrationPolicy(McpClientRegistrationMode Mode)
{
    /// <summary>
    /// With <see cref="McpClientRegistrationMode.Open"/>, the role a client gets: the id of a role, or the exact name of an
    /// instance-wide role. A client registers as pending when it is unset or names no role.
    /// </summary>
    public string? DefaultRole { get; init; }

    /// <summary>How many clients may wait for approval at once; a registration beyond it is refused.</summary>
    public int MaxPendingClients { get; init; } = 100;

    /// <summary>
    /// How many MCP clients there may be at once, counting pending, approved, rejected and those an administrator
    /// registered alike. A client's own registration beyond it is refused in every mode; an administrator's is not.
    /// </summary>
    public int MaxClients { get; init; } = 1000;

    /// <summary>How many clients one client address may register in an hour, on each instance.</summary>
    public int RegistrationsPerHour { get; init; } = 10;

    /// <summary>
    /// How many clients that <see cref="McpClientRegistrationMode.Open"/> approved one user may keep by consenting. A
    /// consent keeps such a client from the cleanup, so a consent that would keep one more is refused. A client counts
    /// while it is there and the user holds a consent to it, also one ended by a session or a password change until the
    /// prune removes it, and for the user who first consented to it for as long as it is there, until that user ends the
    /// consent with <see cref="IMcpClients.EndConsentAsync"/>. One an administrator approved, registered or gave a role
    /// does not count.
    /// </summary>
    public int MaxConsentsPerUser { get; init; } = 20;
}

/// <summary>The registration policy of MCP clients.</summary>
public enum McpClientRegistrationMode
{
    /// <summary>No client registers itself; the endpoint answers 404.</summary>
    Off = 0,

    /// <summary>A registration creates a pending client, which an administrator approves with a role before any token is issued.</summary>
    Approval = 1,

    /// <summary>A registration is approved at once with the default role. For trusted setups.</summary>
    Open = 2,
}
