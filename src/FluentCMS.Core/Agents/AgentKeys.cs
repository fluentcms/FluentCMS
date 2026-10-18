using System.Buffers;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace FluentCMS.Core.Agents;

// A key is 32 random bytes, so its SHA-256 can be stored and looked up directly: there is no dictionary to try against it, and a
// salted slow hash would only slow down every call (SEC-01). The prefix lets people and secret scanners recognise a key.
public static class AgentKeys
{
    public const string Prefix = "fcms_";

    private const int SecretBytes = 32;

    private static readonly SearchValues<char> Alphabet =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_");

    public static int Length { get; } = Prefix.Length + Base64Url.GetEncodedLength(SecretBytes);

    public static string Generate()
    {
        return Prefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SecretBytes));
    }

    // False for anything Generate cannot produce, so a malformed key is refused without a lookup. The base64url check skips
    // whitespace, so the alphabet is checked first.
    public static bool IsWellFormed(string? key)
    {
        return key is not null
            && key.Length == Length
            && key.StartsWith(Prefix, StringComparison.Ordinal)
            && !key.AsSpan(Prefix.Length).ContainsAnyExcept(Alphabet)
            && Base64Url.IsValid(key.AsSpan(Prefix.Length));
    }

    public static byte[] Hash(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return SHA256.HashData(Encoding.UTF8.GetBytes(key));
    }
}
