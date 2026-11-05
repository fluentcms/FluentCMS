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
