using System.Security.Cryptography;

namespace FluentCMS.Core.Webhooks;

// A signing secret is 32 random bytes in the Standard Webhooks form, whsec_ and base64, so a receiver's existing verifier reads it.
// It is protected under its own purpose, so no other stored secret opens it.
public static class WebhookSecrets
{
    public const string Prefix = "whsec_";

    public const string ProtectionPurpose = "FluentCMS.Webhooks.SigningSecret";

    private const int SecretBytes = 32;

    public static string Generate()
    {
        return Prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes));
    }
}
