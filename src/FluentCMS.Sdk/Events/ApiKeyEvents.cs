namespace FluentCMS.Events;

/// <summary>An API key was created. The event never carries the key or its hash.</summary>
/// <param name="KeyId">The id of the key.</param>
/// <param name="Name">Its name.</param>
/// <param name="KeyClass">Its class: <c>delivery</c>, <c>preview</c> or <c>management</c>.</param>
public sealed record ApiKeyCreatedEvent(Guid KeyId, string Name, string KeyClass) : CmsEvent;

/// <summary>An API key was revoked. It is refused from then on.</summary>
/// <param name="KeyId">The id of the key.</param>
/// <param name="Name">Its name.</param>
public sealed record ApiKeyRevokedEvent(Guid KeyId, string Name) : CmsEvent;

/// <summary>An API key's expiry was brought forward. A key expired this way is refused from that time on.</summary>
/// <param name="KeyId">The id of the key.</param>
/// <param name="Name">Its name.</param>
/// <param name="ExpiresAt">When it expires now.</param>
public sealed record ApiKeyExpiryChangedEvent(Guid KeyId, string Name, DateTimeOffset ExpiresAt) : CmsEvent;

/// <summary>
/// An API key was rotated: a new secret replaced the one it had, which is still accepted until
/// <paramref name="PreviousKeyExpiresAt"/>. The event never carries either secret or its hash.
/// </summary>
/// <param name="KeyId">The id of the key.</param>
/// <param name="Name">Its name.</param>
/// <param name="PreviousKeyExpiresAt">Until when the replaced secret is accepted; null when it was refused at once.</param>
public sealed record ApiKeyRotatedEvent(Guid KeyId, string Name, DateTimeOffset? PreviousKeyExpiresAt) : CmsEvent;

/// <summary>The addresses an API key is accepted from were changed.</summary>
/// <param name="KeyId">The id of the key.</param>
/// <param name="Name">Its name.</param>
/// <param name="AllowedAddresses">The addresses and CIDR ranges it is accepted from now; null for any address.</param>
public sealed record ApiKeyAllowedAddressesChangedEvent(Guid KeyId, string Name, IReadOnlyList<string>? AllowedAddresses) : CmsEvent;

/// <summary>A valid API key was refused because the request came from an address the key is not allowed from.</summary>
/// <param name="KeyId">The id of the key.</param>
/// <param name="Name">Its name.</param>
/// <param name="Ip">The client address of the request, as the host trusts it.</param>
public sealed record ApiKeyAddressRefusedEvent(Guid KeyId, string Name, string? Ip) : CmsEvent;
