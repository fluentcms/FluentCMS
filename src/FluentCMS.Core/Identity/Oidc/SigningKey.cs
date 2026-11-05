namespace FluentCMS.Core.Identity.Oidc;

// A row of cms_signing_key: a key the authorization server generated and keeps itself, protected with Data Protection.
// Signing keys are used only when no certificate is configured; the encryption key of codes and refresh tokens always.
internal sealed class SigningKey
{
    public const string Signing = "sig";
    public const string Encryption = "enc";

    public Guid Id { get; set; }

    public string Use { get; set; } = "";

    public string Algorithm { get; set; } = "";

    public string ProtectedKey { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
}

// A row of cms_data_protection_key: one key of the Data Protection key ring, as the XML Data Protection writes.
internal sealed class DataProtectionKey
{
    public Guid Id { get; set; }

    public string? FriendlyName { get; set; }

    public string Xml { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
}
