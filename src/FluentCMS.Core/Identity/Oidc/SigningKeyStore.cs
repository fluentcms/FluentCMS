using System.Security.Cryptography;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace FluentCMS.Core.Identity.Oidc;

// The keys of cms_signing_key. A use gets its first key the first time one is asked for, and a new one whenever none of
// its keys is left that has not retired; that key is active at once. Instances that start together on an empty table race
// for one row: each inserts the next sequence of the use, the unique index lets one in, and the others read the winner's
// key. The ids are random, so a kid never names two keys.
internal sealed class SigningKeyStore(IServiceScopeFactory scopes, IDataProtectionProvider protection, TimeProvider time)
{
    private const int RsaKeySize = 2048;
    private const int EncryptionKeySize = 32;

    private readonly IDataProtector _protector = protection.CreateProtector("FluentCMS.SigningKeys");

    // Every row, by use and sequence, after making a key for each of the uses that has none that has not retired.
    public IReadOnlyList<SigningKey> Read(IReadOnlyCollection<string> uses)
    {
        using var scope = scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        var stored = Stored(context).ToList();
        while (Missing(stored, uses) is { } generated)
        {
            context.Add(generated);
            try
            {
                context.SaveChanges();
            }
            catch (DbUpdateException) when (Exists(generated))
            {
                // Another instance stored the key of this sequence in the meantime; it is the one to use.
            }

            context.ChangeTracker.Clear();
            stored = Stored(context).ToList();
        }

        return stored;
    }

    public async Task<IReadOnlyList<SigningKey>> ReadAsync(IReadOnlyCollection<string> uses, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        var stored = await Stored(context).ToListAsync(cancellationToken);
        while (Missing(stored, uses) is { } generated)
        {
            context.Add(generated);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException) when (Exists(generated))
            {
            }

            context.ChangeTracker.Clear();
            stored = await Stored(context).ToListAsync(cancellationToken);
        }

        return stored;
    }

    // The key of a row, or null when the key ring cannot read it: the ring key that protected it is gone, or the row is
    // not one this server wrote.
    public SecurityKey? Unprotect(SigningKey row)
    {
        byte[] material;
        try
        {
            material = _protector.Unprotect(Convert.FromBase64String(row.ProtectedKey));
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return null;
        }

        SecurityKey key;
        if (row.Use == SigningKey.Signing)
        {
            using var rsa = RSA.Create();
            rsa.ImportRSAPrivateKey(material, out _);
            CryptographicOperations.ZeroMemory(material);
            key = new RsaSecurityKey(rsa.ExportParameters(includePrivateParameters: true));
        }
        else
        {
            key = new SymmetricSecurityKey(material);
        }

        key.KeyId = KeyId(row);
        return key;
    }

    public static string KeyId(SigningKey row) => row.Id.ToString("N");

    // The first key of a use, or the one after its last has retired, active at once.
    private SigningKey? Missing(List<SigningKey> stored, IReadOnlyCollection<string> uses)
    {
        var now = time.GetUtcNow();
        return uses.FirstOrDefault(use => !stored.Exists(key => key.Use == use && !SigningKeyStates.IsRetired(key, now))) is { } use
            ? Generate(use, NextSequence(stored, use), now, activeFrom: now)
            : null;
    }

    private static int NextSequence(IEnumerable<SigningKey> stored, string use) =>
        stored.Where(key => key.Use == use).Select(key => key.Sequence).DefaultIfEmpty().Max() + 1;

    private static IQueryable<SigningKey> Stored(SystemDbContext context) =>
        context.Set<SigningKey>().AsNoTracking().OrderBy(key => key.Use).ThenBy(key => key.Sequence);

    private bool Exists(SigningKey generated)
    {
        using var scope = scopes.CreateScope();
        return scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<SigningKey>().Any(key => key.Use == generated.Use && key.Sequence == generated.Sequence);
    }

    private SigningKey Generate(string use, int sequence, DateTimeOffset now, DateTimeOffset activeFrom)
    {
        byte[] material;
        string algorithm;
        if (use == SigningKey.Signing)
        {
            using var rsa = RSA.Create(RsaKeySize);
            material = rsa.ExportRSAPrivateKey();
            algorithm = SecurityAlgorithms.RsaSha256;
        }
        else
        {
            material = RandomNumberGenerator.GetBytes(EncryptionKeySize);
            algorithm = SecurityAlgorithms.Aes256KW;
        }

        try
        {
            return new SigningKey
            {
                Id = Guid.NewGuid(),
                Use = use,
                Sequence = sequence,
                Algorithm = algorithm,
                ProtectedKey = Convert.ToBase64String(_protector.Protect(material)),
                CreatedAt = now,
                ActiveFrom = activeFrom,
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material);
        }
    }
}

// Whether tokens are signed with a key the server generated, which Production refuses unless the host allows it.
internal sealed class SigningKeyStatus(SecurityOptions security) : ISigningKeyStatus
{
    public bool IsGenerated => !security.SigningCertificate.IsConfigured;
}
