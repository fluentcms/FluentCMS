using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace FluentCMS.Admin.Authentication;

// The random secrets of a sign-in and the PKCE challenge (RFC 7636). On WebAssembly the runtime answers RandomNumberGenerator
// from the browser's crypto.getRandomValues and SHA-256 with its managed implementation, so both work there.
internal static class ProofKey
{
    // 32 bytes, 43 characters of base64url: the length RFC 7636 asks of a verifier, and as random as a state needs.
    public static string NewSecret() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static string Challenge(string verifier) => Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    // In constant time, so that a guessed state learns nothing from how long the comparison took.
    public static bool SameSecret(string? sent, string issued) =>
        sent is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(sent), Encoding.UTF8.GetBytes(issued));
}
