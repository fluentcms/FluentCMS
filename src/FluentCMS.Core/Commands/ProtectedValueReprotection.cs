using System.Security.Cryptography;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Core.Storage;
using FluentCMS.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Commands;

// A value a plugin named that the key ring cannot unprotect, by where it is stored and never by what is.
internal sealed record ProtectedValueLeft(string Table, string Id, string Column);

// The keys of cms_signing_key that are still accepted and not under the default key of the ring: Older under another key
// the ring has, Unreadable under one it cannot unprotect. Rotation replaces them (ADR 0008), so the pass only counts them.
internal sealed record TokenKeysLeft(int Older, int Unreadable);

internal sealed record ProtectedValuesReprotected(int Values, IReadOnlyList<ProtectedValueLeft> Left, TokenKeysLeft TokenKeys);

// Protects every value the plugins name through IProtectedValueSource again under the current default key of the key ring,
// with the rules of SecretSettingsReprotection: a value already under that key is not written, one the ring cannot
// unprotect is left as it is, and each is written only over what was read, which is read again when another writer got
// there first. Data Protection makes every protected value new, so what was read stands for the version of the value.
internal sealed class ProtectedValueReprotection(IDataProtectionProvider provider, IEnumerable<IProtectedValueSource> sources, SystemDbContext context, TimeProvider time)
{
    private const int Attempts = 100;

    public async Task<ProtectedValuesReprotected> RunAsync(CancellationToken cancellationToken)
    {
        var values = 0;
        List<ProtectedValueLeft> left = [];
        foreach (var source in sources)
        {
            foreach (var value in await source.ReadAsync(cancellationToken))
            {
                switch (await ReprotectAsync(source, value, cancellationToken))
                {
                    case true:
                        values++;
                        break;
                    case null:
                        left.Add(new ProtectedValueLeft(value.Table, value.Id, value.Column));
                        break;
                }
            }
        }

        return new ProtectedValuesReprotected(values, [.. left.OrderBy(item => item.Table, StringComparer.Ordinal).ThenBy(item => item.Id, StringComparer.Ordinal).ThenBy(item => item.Column, StringComparer.Ordinal)], await CountTokenKeysAsync(cancellationToken));
    }

    // True when the value was written again, false when there was nothing to do, null when the ring cannot unprotect it.
    private async Task<bool?> ReprotectAsync(IProtectedValueSource source, ProtectedValue value, CancellationToken cancellationToken)
    {
        var current = value;
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            if (!TryReprotect(current, out var reprotected))
            {
                return null;
            }

            if (reprotected is null)
            {
                return false;
            }

            if (await source.WriteAsync(current, reprotected, cancellationToken))
            {
                return true;
            }

            // Another writer got there first; what it wrote is checked again.
            if ((await source.ReadAsync(cancellationToken)).FirstOrDefault(item => item.Table == value.Table && item.Id == value.Id && item.Column == value.Column) is not { } again)
            {
                return false;
            }

            current = again;
        }

        throw new InvalidOperationException($"The value '{value.Column}' of {value.Table} row {value.Id} kept changing while it was protected again; run the pass again.");
    }

    // False when the ring cannot unprotect the value; otherwise reprotected is the value under the default key, or null when
    // it is under that key already, as Data Protection itself tells. A protector that cannot say is taken to need it.
    private bool TryReprotect(ProtectedValue value, out string? reprotected)
    {
        reprotected = null;
        byte[]? plain = null;
        try
        {
            var protector = provider.CreateProtector(value.Purpose);
            var stored = WebEncoders.Base64UrlDecode(value.Stored);
            var migrate = true;
            plain = protector is IPersistedDataProtector persisted
                ? persisted.DangerousUnprotect(stored, ignoreRevocationErrors: false, out migrate, out _)
                : protector.Unprotect(stored);
            if (migrate)
            {
                reprotected = WebEncoders.Base64UrlEncode(protector.Protect(plain));
            }

            return true;
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return false;
        }
        finally
        {
            if (plain is not null)
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
    }

    // A retired key is no longer accepted and the job oidc.keys removes it, so it needs no ring key.
    private async Task<TokenKeysLeft> CountTokenKeysAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var protector = provider.CreateProtector(SigningKeyStore.Purpose);
        var keys = await context.Set<SigningKey>().AsNoTracking().ToListAsync(cancellationToken);
        var (older, unreadable) = (0, 0);
        foreach (var key in keys.Where(key => !SigningKeyStates.IsRetired(key, now)))
        {
            byte[]? material = null;
            try
            {
                var migrate = true;
                var stored = Convert.FromBase64String(key.ProtectedKey);
                material = protector is IPersistedDataProtector persisted
                    ? persisted.DangerousUnprotect(stored, ignoreRevocationErrors: false, out migrate, out _)
                    : protector.Unprotect(stored);
                older += migrate ? 1 : 0;
            }
            catch (Exception exception) when (exception is CryptographicException or FormatException)
            {
                unreadable++;
            }
            finally
            {
                if (material is not null)
                {
                    CryptographicOperations.ZeroMemory(material);
                }
            }
        }

        return new TokenKeysLeft(older, unreadable);
    }
}
