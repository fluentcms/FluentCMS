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

    /// <summary>How many clients one client address may register in an hour, on each instance.</summary>
    public int RegistrationsPerHour { get; init; } = 10;
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
