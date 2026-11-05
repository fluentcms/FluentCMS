using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using FluentCMS.Core.Authorization;

namespace FluentCMS.Core.Identity.ApiKeys;

// fcms_dk_, fcms_pk_ or fcms_mk_ and 32 random bytes in base64url (spec 4.7).
internal static class ApiKeyFormat
{
    public const int SecretBytes = 32;

    // The class marker and the first 8 characters of the random part: 48 bits, so prefixes rarely repeat.
    public const int LookupLength = 16;

    private const int MarkerLength = 8;
    private static readonly int SecretLength = MarkerLength + Base64Url.GetEncodedLength(SecretBytes);

    public static string Marker(ApiKeyClass keyClass) => keyClass switch
    {
        ApiKeyClass.Delivery => "fcms_dk_",
        ApiKeyClass.Preview => "fcms_pk_",
        ApiKeyClass.Management => "fcms_mk_",
        _ => throw new ArgumentOutOfRangeException(nameof(keyClass), keyClass, null),
    };

    public static string Scope(ApiKeyClass keyClass) => keyClass switch
    {
        ApiKeyClass.Delivery => TokenScopes.Delivery,
        ApiKeyClass.Preview => TokenScopes.Preview,
        ApiKeyClass.Management => TokenScopes.Management,
        _ => throw new ArgumentOutOfRangeException(nameof(keyClass), keyClass, null),
    };

    public static string Generate(ApiKeyClass keyClass) =>
        Marker(keyClass) + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SecretBytes));

    // Only the shape: whether the key is one is for the stored hash to say.
    public static bool IsWellFormed(string secret) =>
        secret.Length == SecretLength
        && Enum.GetValues<ApiKeyClass>().Any(keyClass => secret.StartsWith(Marker(keyClass), StringComparison.Ordinal))
        && Base64Url.IsValid(secret.AsSpan(MarkerLength));

    public static string LookupPrefix(string secret) => secret[..LookupLength];

    public static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    public static bool Matches(string secret, string storedHash) =>
        storedHash.Length == SHA256.HashSizeInBytes * 2
        && CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(secret)), Convert.FromHexString(storedHash));
}
