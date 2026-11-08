using FluentCMS.Core.Caching;
using FluentCMS.Providers;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.KeyManagement.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Identity.Oidc;

// Data Protection makes a key when its ring has none it can use. Instances that start together on an empty ring would each
// make one, and an instance that read only its own keeps it for up to a day, so it refuses the others' cookies and payloads
// once the two minutes in which Data Protection reloads the ring for an unknown key are over. So a key is made under the
// lock, after reading the ring again, and an instance that finds a key another made in the meantime uses that one instead.
internal sealed class LockedKeyManager(IKeyManager inner, IDefaultKeyResolver resolver, IDistributedLock locks, ILogger<LockedKeyManager> logger) : IKeyManager
{
    public const string LockName = "fluentcms:data-protection-key";

    private const string Work = "making a Data Protection key";

    // Data Protection's call blocks a thread for this wait, so it covers a live holder, which makes its key in well under
    // a second, and not one that stopped without releasing the lock: past it, a key is made without the lock, as
    // before the lock existed.
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    // A key made this recently was made by an instance that raced this one, within the clock skew Data Protection allows
    // between instances. An older one was in the ring the caller read, so the caller asked for a new key on purpose.
    private static readonly TimeSpan Recent = TimeSpan.FromMinutes(5);

    public IKey CreateNewKey(DateTimeOffset activationDate, DateTimeOffset expirationDate)
    {
        var asked = DateTimeOffset.UtcNow;

        // Synchronous, as Data Protection asks for the key; the wait is for another instance to make one.
        var held = locks.TryAcquireAsync(LockName, Wait).GetAwaiter().GetResult();
        if (held is null)
        {
            LockLog.WentOnWithoutLock(logger, LockName, Wait, Work);
            return inner.CreateNewKey(activationDate, expirationDate);
        }

        try
        {
            if (resolver.ResolveDefaultKeyPolicy(activationDate, inner.GetAllKeys()).DefaultKey is { } made && made.CreationDate >= asked - Recent)
            {
                return made;
            }

            return inner.CreateNewKey(activationDate, expirationDate);
        }
        finally
        {
            held.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    public IReadOnlyCollection<IKey> GetAllKeys() => inner.GetAllKeys();

    public CancellationToken GetCacheExpirationToken() => inner.GetCacheExpirationToken();

    public void RevokeKey(Guid keyId, string? reason = null) => inner.RevokeKey(keyId, reason);

    public void RevokeAllKeys(DateTimeOffset revocationDate, string? reason = null) => inner.RevokeAllKeys(revocationDate, reason);

    // Wraps the key manager Data Protection, or the host before it, registered.
    public static void Register(IServiceCollection services)
    {
        var registered = services.Last(service => service.ServiceType == typeof(IKeyManager));
        services.Remove(registered);
        services.AddSingleton<IKeyManager>(provider => ActivatorUtilities.CreateInstance<LockedKeyManager>(provider, Create(provider, registered)));
    }

    private static IKeyManager Create(IServiceProvider provider, ServiceDescriptor registered) =>
        (IKeyManager)(registered.ImplementationInstance
            ?? registered.ImplementationFactory?.Invoke(provider)
            ?? ActivatorUtilities.CreateInstance(provider, registered.ImplementationType!));
}
