using System.Security.Cryptography;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace FluentCMS.Core.Identity.Oidc;

// The keys of cms_signing_key. A use gets its first key the first time one is asked for, and a new one whenever none of
// its keys is left that has not retired; that key is active at once. Instances that start together on an empty table race
// for one row: each inserts the next sequence of the use, the unique index lets one in, and the others read the winner's
// key. The ids are random, so a kid never names two keys.
internal sealed class SigningKeyStore(IServiceScopeFactory scopes, IDataProtectionProvider protection, TimeProvider time, ILogger<SigningKeyStore> logger)
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
                OidcLog.KeyMade(logger, generated.Id, generated.Use, generated.Sequence, generated.ActiveFrom);
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
                OidcLog.KeyMade(logger, generated.Id, generated.Use, generated.Sequence, generated.ActiveFrom);
            }
            catch (DbUpdateException) when (Exists(generated))
            {
            }

            context.ChangeTracker.Clear();
            stored = await Stored(context).ToListAsync(cancellationToken);
        }

        return stored;
    }

    // Makes the successor of the active key of a use: accepted from now and active after the lead, with the keys it follows
    // retiring an overlap after that. While a key of the use is upcoming it makes none and returns that one, so of two that
    // rotate at once, the job on two instances or the job and a command, the second finds the first's key.
    public async Task<KeyRotation> RotateAsync(string use, TimeSpan lead, TimeSpan overlap, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        var keys = await context.Set<SigningKey>().Where(key => key.Use == use).OrderBy(key => key.Sequence).ToListAsync(cancellationToken);
        var now = time.GetUtcNow();
        var accepted = keys.Where(key => !SigningKeyStates.IsRetired(key, now)).ToList();
        if (accepted.Find(key => key.ActiveFrom > now) is { } upcoming)
        {
            return new KeyRotation(upcoming, Made: false);
        }

        // A key that retires within the allowance for clocks is as good as retired, and may be one that a revocation retired
        // on a machine whose clock is ahead: it is given no more time. With no other key to follow there is nothing to
        // publish the new one ahead of, so it is active at once.
        var lasting = accepted.Where(key => !RetiresSoon(key, now)).ToList();
        var made = Generate(use, NextSequence(keys, use), now, lasting.Count == 0 ? now : now + lead);
        var retires = made.ActiveFrom + overlap;
        List<SigningKey> followed = [];
        foreach (var key in lasting)
        {
            // A key that has its date keeps it. Only the newest can have one too early: a generated key that a certificate
            // replaced and that signs again.
            if (key.RetiredAt is null || (key == keys[^1] && key.RetiredAt < retires))
            {
                key.RetiredAt = retires;
                followed.Add(key);
            }
        }

        context.Add(made);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (Exists(made))
        {
            await using var again = scopes.CreateAsyncScope();
            var winner = await again.ServiceProvider.GetRequiredService<SystemDbContext>().Set<SigningKey>().AsNoTracking()
                .SingleAsync(key => key.Use == use && key.Sequence == made.Sequence, cancellationToken);
            return new KeyRotation(winner, Made: false);
        }

        OidcLog.KeyMade(logger, made.Id, made.Use, made.Sequence, made.ActiveFrom);
        foreach (var key in followed)
        {
            OidcLog.KeyRetirementSet(logger, key.Id, key.Use, retires);
        }

        return new KeyRotation(made, Made: true);
    }

    // For a key that is suspected, the opposite of a rotation: every stored key of the uses stops being accepted now,
    // upcoming ones included and none of them read, so a row the key ring cannot read goes too; and each use the server
    // generates gets a new key that is active at once, with no lead. A revoked key retires when its successor becomes active,
    // which is how UpkeepAsync knows never to give it more time.
    public async Task<KeyRevocation> RevokeAsync(IReadOnlyCollection<string> uses, IReadOnlyCollection<string> generated, CancellationToken cancellationToken)
    {
        while (true)
        {
            await using var scope = scopes.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
            var keys = await context.Set<SigningKey>().OrderBy(key => key.Use).ThenBy(key => key.Sequence).ToListAsync(cancellationToken);
            var now = time.GetUtcNow();

            var retired = keys.Where(key => uses.Contains(key.Use) && !SigningKeyStates.IsRetired(key, now)).ToList();
            foreach (var key in retired)
            {
                key.RetiredAt = now;
            }

            List<SigningKey> made = [.. uses.Where(generated.Contains).Select(use => Generate(use, NextSequence(keys, use), now, activeFrom: now))];
            context.AddRange(made);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException) when (made.Exists(Exists))
            {
                // A rotation made a key of the same sequence in the meantime; it is retired with the rest on the next pass.
                continue;
            }

            foreach (var key in made)
            {
                OidcLog.KeyMade(logger, key.Id, key.Use, key.Sequence, key.ActiveFrom);
            }

            return new KeyRevocation(retired, made, now);
        }
    }

    // How many keys of the uses a revocation would retire.
    public async Task<int> CountAcceptedAsync(IReadOnlyCollection<string> uses, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        return (await ReadAsync([], cancellationToken)).Count(key => uses.Contains(key.Use) && !SigningKeyStates.IsRetired(key, now));
    }

    // What the job does that needs no new key. It removes the rows that have retired, material included. While a certificate
    // signs it gives each generated signing key without a date the overlap from this run. And it moves the retirement of a
    // key later when the overlap has grown since its successor was made, never earlier.
    //
    // A revocation retires keys at once, and the job must never give one its time back, on an instance whose clock is behind
    // the machine that revoked or when the revocation commits between the job's read and its write. So it leaves alone
    // every retired_at that is not later than its own clock and the allowance for clocks, whichever place the key has among
    // the others, and a key that retired no later than its successor became active. And each write names the retired_at it
    // read: one that finds another date changes nothing.
    public async Task UpkeepAsync(bool certificateSigns, TimeSpan overlap, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        var keys = await Stored(context).ToListAsync(cancellationToken);
        var now = time.GetUtcNow();

        foreach (var key in keys.Where(key => SigningKeyStates.IsRetired(key, now)))
        {
            if (await AsRead(context, key).ExecuteDeleteAsync(cancellationToken) > 0)
            {
                OidcLog.KeyRemoved(logger, key.Id, key.Use, key.CreatedAt, key.RetiredAt!.Value);
            }
        }

        foreach (var use in SigningKey.Uses)
        {
            var kept = keys.Where(key => key.Use == use && !SigningKeyStates.IsRetired(key, now)).ToList();
            for (var index = 0; index < kept.Count; index++)
            {
                var key = kept[index];
                DateTimeOffset? retires = null;
                if (index + 1 < kept.Count)
                {
                    var successor = kept[index + 1];
                    if (key.RetiredAt is null || (!RetiresSoon(key, now) && key.RetiredAt > successor.ActiveFrom && key.RetiredAt < successor.ActiveFrom + overlap))
                    {
                        retires = successor.ActiveFrom + overlap;
                    }
                }
                else if (key.RetiredAt is null && certificateSigns && use == SigningKey.Signing)
                {
                    retires = now + overlap;
                }

                if (retires is not null && await AsRead(context, key).ExecuteUpdateAsync(row => row.SetProperty(column => column.RetiredAt, retires), cancellationToken) > 0)
                {
                    OidcLog.KeyRetirementSet(logger, key.Id, key.Use, retires.Value);
                }
            }
        }
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

    // Whether the key retires within the allowance for clocks, by this instance's clock.
    private static bool RetiresSoon(SigningKey key, DateTimeOffset now) => key.RetiredAt is { } retires && retires <= now + KeyOverlap.ClockAllowance;

    // The row while it still has the retired_at that was read.
    private static IQueryable<SigningKey> AsRead(SystemDbContext context, SigningKey read) =>
        context.Set<SigningKey>().Where(key => key.Id == read.Id && key.RetiredAt == read.RetiredAt);

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

// The upcoming key of a use: the one a rotation made, or the one that was there already.
internal sealed record KeyRotation(SigningKey Key, bool Made);

// What a revocation did to cms_signing_key.
internal sealed record KeyRevocation(IReadOnlyList<SigningKey> Retired, IReadOnlyList<SigningKey> Made, DateTimeOffset At);

// Whether tokens are signed with a key the server generated, which Production refuses unless the host allows it.
internal sealed class SigningKeyStatus(SecurityOptions security) : ISigningKeyStatus
{
    public bool IsGenerated => !security.SigningCertificate.IsConfigured;
}
