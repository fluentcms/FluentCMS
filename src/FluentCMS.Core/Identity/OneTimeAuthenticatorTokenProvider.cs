using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Core.Identity;

// Identity's authenticator codes (RFC 6238: SHA-1, 30 second steps, six digits, two steps either way), on the host's clock,
// each taken once: the step of the last code taken is kept with the user, in the save that takes it, and a code of that
// step or an earlier one is refused, so a code seen over a shoulder or in a log is spent once the user has used it. Of two
// requests that bring one code at once, the second save fails on the user's concurrency stamp or, for the first code a user
// gives, on the token row's key, whichever its commands meet first; CmsUserManager makes both a refusal.
internal sealed class OneTimeAuthenticatorTokenProvider(TimeProvider time) : IUserTwoFactorTokenProvider<CmsUser>
{
    // UserStoreBase keeps the authenticator key under this provider name too.
    private const string StoreProvider = "[AspNetUserStore]";
    internal const string LastStepName = "AuthenticatorLastStep";
    private const int Window = 2;

    public Task<string> GenerateAsync(string purpose, UserManager<CmsUser> manager, CmsUser user) => Task.FromResult("");

    public async Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<CmsUser> manager, CmsUser user)
    {
        ArgumentNullException.ThrowIfNull(manager);

        return !string.IsNullOrWhiteSpace(await manager.GetAuthenticatorKeyAsync(user));
    }

    public async Task<bool> ValidateAsync(string purpose, string token, UserManager<CmsUser> manager, CmsUser user)
    {
        ArgumentNullException.ThrowIfNull(manager);

        if (token is not { Length: 6 } || !token.All(char.IsAsciiDigit)
            || await manager.GetAuthenticatorKeyAsync(user) is not { Length: > 0 } key
            || Base32(key) is not { Length: > 0 } secret)
        {
            return false;
        }

        var last = long.TryParse(await manager.GetAuthenticationTokenAsync(user, StoreProvider, LastStepName), NumberStyles.None, CultureInfo.InvariantCulture, out var stored) ? stored : long.MinValue;
        var now = time.GetUtcNow().ToUnixTimeSeconds() / 30;
        for (var step = Math.Max(now - Window, last + 1); step <= now + Window; step++)
        {
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Code(secret, step)), Encoding.ASCII.GetBytes(token)))
            {
                return (await manager.SetAuthenticationTokenAsync(user, StoreProvider, LastStepName, step.ToString(CultureInfo.InvariantCulture))).Succeeded;
            }
        }

        return false;
    }

    private static string Code(byte[] secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
#pragma warning disable CA5350 // RFC 6238 and every authenticator app use HMAC-SHA1.
        var hash = HMACSHA1.HashData(secret, counter);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0f;
        var value = (BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset)) & 0x7fffffff) % 1_000_000;
        return value.ToString("D6", CultureInfo.InvariantCulture);
    }

    private static byte[]? Base32(string text)
    {
        const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        List<byte> bytes = [];
        int buffer = 0, bits = 0;
        foreach (var character in text.TrimEnd('=').ToUpperInvariant())
        {
            var index = Alphabet.IndexOf(character, StringComparison.Ordinal);
            if (index < 0)
            {
                return null;
            }

            buffer = ((buffer << 5) | index) & 0xffff;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }

        return [.. bytes];
    }
}
