using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

namespace FluentCMS.Admin.Testing;

// RFC 6238 with the defaults of an authenticator app: SHA-1, six digits, 30 seconds, from the key in base32 as the server
// gives it, in any case and with the spaces of its groups.
internal static class Totp
{
    public static string Code(string key, DateTimeOffset at)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, at.ToUnixTimeSeconds() / 30);
#pragma warning disable CA5350 // TOTP is defined on HMAC-SHA1.
        var hash = HMACSHA1.HashData(Base32(key), counter);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var value = (BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset)) & 0x7FFFFFFF) % 1_000_000;
        return value.ToString("D6", CultureInfo.InvariantCulture);
    }

    private static byte[] Base32(string text)
    {
        const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        List<byte> bytes = [];
        int buffer = 0, bits = 0;
        foreach (var c in text.Replace(" ", "", StringComparison.Ordinal).TrimEnd('=').ToUpperInvariant())
        {
            buffer = (buffer << 5) | Alphabet.IndexOf(c, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)(buffer >> bits));
            }
        }

        return [.. bytes];
    }
}
