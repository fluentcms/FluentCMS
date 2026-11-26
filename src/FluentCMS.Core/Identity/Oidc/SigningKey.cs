namespace FluentCMS.Core.Identity.Oidc;

// A row of cms_signing_key: a key the authorization server generated and keeps itself, protected with Data Protection.
// Signing keys sign only when no certificate is configured; the encryption key of codes and refresh tokens always. The
// dates are written once, by whoever made the key's successor, so every instance works out the same state from the same
// rows and its clock (ADR 0008).
internal sealed class SigningKey
{
    public const string Signing = "sig";
    public const string Encryption = "enc";

    public static readonly string[] Uses = [Signing, Encryption];

    public Guid Id { get; set; }

    public string Use { get; set; } = "";

    // Rises by one with each key of a use; the unique index on use and sequence lets one of two makers in.
    public int Sequence { get; set; }

    public string Algorithm { get; set; } = "";

    public string ProtectedKey { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    // From when the key signs or encrypts, unless a later key does.
    public DateTimeOffset ActiveFrom { get; set; }

    // From when the key is no longer accepted. Unset while it has no successor.
    public DateTimeOffset? RetiredAt { get; set; }
}

internal enum SigningKeyState
{
    Upcoming,
    Active,
    Retiring,
    Retired,
}

internal static class SigningKeyStates
{
    // The key of a use that signs or encrypts at this moment: the one with the highest sequence whose active_from has
    // passed and that has not retired.
    public static SigningKey? ActiveOf(IEnumerable<SigningKey> keysOfUse, DateTimeOffset now) =>
        keysOfUse.Where(key => !IsRetired(key, now) && key.ActiveFrom <= now).MaxBy(key => key.Sequence);

    public static SigningKeyState StateOf(SigningKey key, SigningKey? active, DateTimeOffset now) =>
        IsRetired(key, now) ? SigningKeyState.Retired
        : key.ActiveFrom > now ? SigningKeyState.Upcoming
        : ReferenceEquals(key, active) || key.Id == active?.Id ? SigningKeyState.Active
        : SigningKeyState.Retiring;

    public static bool IsRetired(SigningKey key, DateTimeOffset now) => key.RetiredAt is { } retired && retired <= now;
}

// A row of cms_data_protection_key: one key of the Data Protection key ring, as the XML Data Protection writes.
internal sealed class DataProtectionKey
{
    public Guid Id { get; set; }

    public string? FriendlyName { get; set; }

    public string Xml { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
}
