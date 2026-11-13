using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluentCMS.Core.Identity;

// Identity's Data Protection tokens on the host's TimeProvider. In .NET 10 DataProtectorTokenProvider reads
// DateTimeOffset.UtcNow when it makes a token and when it checks one, and takes no TimeProvider, so both are written again
// here in its format: the time the token was made, the user's id, the purpose and the security stamp, protected under the
// provider's name. A token Identity's provider made is good here, and the other way round, with the same lifetime.
internal abstract class ClockedTokenProvider<TUser>(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<DataProtectionTokenProviderOptions> options,
    TimeProvider time,
    ILogger<DataProtectorTokenProvider<TUser>> logger)
    : DataProtectorTokenProvider<TUser>(dataProtectionProvider, options, logger)
    where TUser : class
{
    private static readonly UTF8Encoding Encoding = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    protected TimeProvider Time { get; } = time;

    public override async Task<string> GenerateAsync(string purpose, UserManager<TUser> manager, TUser user)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(user);

        var userId = await manager.GetUserIdAsync(user);
        var stamp = manager.SupportsUserSecurityStamp ? await manager.GetSecurityStampAsync(user) : null;
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding, leaveOpen: true))
        {
            writer.Write(Time.GetUtcNow().UtcTicks);
            writer.Write(userId);
            writer.Write(purpose ?? "");
            writer.Write(stamp ?? "");
        }

        return Convert.ToBase64String(Protector.Protect(stream.ToArray()));
    }

    public override async Task<bool> ValidateAsync(string purpose, string token, UserManager<TUser> manager, TUser user)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(user);

        if (Read(token) is not { } read
            || read.MadeAt + Options.TokenLifespan < Time.GetUtcNow()
            || read.UserId != await manager.GetUserIdAsync(user)
            || !string.Equals(read.Purpose, purpose, StringComparison.Ordinal))
        {
            return false;
        }

        return manager.SupportsUserSecurityStamp
            ? read.Stamp == await manager.GetSecurityStampAsync(user)
            : read.Stamp.Length == 0;
    }

    private (DateTimeOffset MadeAt, string UserId, string Purpose, string Stamp)? Read(string token)
    {
        try
        {
            using var stream = new MemoryStream(Protector.Unprotect(Convert.FromBase64String(token)));
            using var reader = new BinaryReader(stream, Encoding);
            var read = (new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero), reader.ReadString(), reader.ReadString(), reader.ReadString());
            return stream.Position == stream.Length ? read : null;
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException or IOException or ArgumentException)
        {
            return null;
        }
    }
}
