using System.Security.Cryptography;
using System.Text;
using FluentCMS.Providers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluentCMS.Plugins.Media;

// The key that signs transform URLs: FluentCms:Media:SigningKey when it is set, otherwise one 256-bit key generated once and
// kept in cms_media_signing_key, protected by Data Protection. The key ring protects it at rest only, so signed URLs outlive
// a rotation of the ring; the instances that start together create it under a lock and share it. Once read, an instance
// keeps it until it stops, so replacing the key needs every instance restarted, and invalidates every URL signed before.
internal sealed class MediaSigning(
    IServiceScopeFactory scopes,
    IDistributedLock locks,
    IDataProtectionProvider protection,
    IOptions<MediaOptions> options,
    TimeProvider time,
    ILogger<MediaSigning> logger) : IDisposable
{
    public const string LockName = "fluentcms:media-signing-key";
    public const int KeyId = 1;
    public const int KeyLength = 32;

    // Long enough for a holder that stopped without releasing the lock to lose its lease, as for SuperAdminGuard.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(45);

    private readonly IDataProtector _protector = protection.CreateProtector("FluentCMS.Plugins.Media.SigningKey");
    private readonly byte[]? _configured = options.Value.SigningKeyBytes;
    private readonly SemaphoreSlim _loading = new(1, 1);
    private byte[]? _key;

    public async Task<string> SignAsync(string canonical, CancellationToken cancellationToken) =>
        WebEncoders.Base64UrlEncode(HMACSHA256.HashData(await KeyAsync(cancellationToken), Encoding.UTF8.GetBytes(canonical)));

    public async Task<bool> VerifyAsync(string canonical, string? signature, CancellationToken cancellationToken)
    {
        byte[] given;
        try
        {
            given = signature is { Length: > 0 } ? WebEncoders.Base64UrlDecode(signature) : [];
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = HMACSHA256.HashData(await KeyAsync(cancellationToken), Encoding.UTF8.GetBytes(canonical));
        return CryptographicOperations.FixedTimeEquals(expected, given);
    }

    public async Task<byte[]> KeyAsync(CancellationToken cancellationToken)
    {
        if (_configured is { } configured)
        {
            return configured;
        }

        if (Volatile.Read(ref _key) is { } key)
        {
            return key;
        }

        await _loading.WaitAsync(cancellationToken);
        try
        {
            if (_key is null)
            {
                await using var scope = scopes.CreateAsyncScope();
                _key = await LoadAsync(scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>(), cancellationToken);
            }

            return _key;
        }
        finally
        {
            _loading.Release();
        }
    }

    public void Dispose() => _loading.Dispose();

    private async Task<byte[]> LoadAsync(PluginDbContext<MediaPlugin> database, CancellationToken cancellationToken)
    {
        if (await StoredAsync(database, cancellationToken) is { } stored)
        {
            return stored;
        }

        await using var handle = await locks.TryAcquireAsync(LockName, Wait, cancellationToken)
            ?? throw new TimeoutException($"The lock {LockName} was not free within {Wait.TotalSeconds} seconds, so the media signing key was not created.");
        if (await StoredAsync(database, cancellationToken) is { } created)
        {
            return created;
        }

        var key = RandomNumberGenerator.GetBytes(KeyLength);
        database.Add(new SigningKey { Id = KeyId, Key = _protector.Protect(Convert.ToBase64String(key)), CreatedAt = time.GetUtcNow() });
        await database.SaveChangesAsync(cancellationToken);
        MediaLog.SigningKeyCreated(logger);
        return key;
    }

    private async Task<byte[]?> StoredAsync(PluginDbContext<MediaPlugin> database, CancellationToken cancellationToken)
    {
        var row = await database.Set<SigningKey>().AsNoTracking().FirstOrDefaultAsync(item => item.Id == KeyId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(_protector.Unprotect(row.Key));
        }
        catch (CryptographicException exception)
        {
            MediaLog.SigningKeyUnreadable(logger, exception);
            throw new InvalidOperationException(
                $"The media signing key in {MediaModel.SigningKeyTable} was protected with a Data Protection key this instance does not have. Restore the key ring, set {MediaOptions.Section}:SigningKey, or delete the row to create a new key, which invalidates every signed URL.",
                exception);
        }
    }
}
