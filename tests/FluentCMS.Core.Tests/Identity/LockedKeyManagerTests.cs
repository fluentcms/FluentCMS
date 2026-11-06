using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Providers;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption.ConfigurationModel;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.KeyManagement.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Identity;

public sealed class LockedKeyManagerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public void AKeyAnotherInstanceJustMadeIsUsedInsteadOfANewOne()
    {
        var made = new FakeKey(Now - TimeSpan.FromSeconds(1));
        var inner = new FakeKeyManager();
        var locks = new FakeLock(grants: true);

        var key = Manager(inner, made, locks).CreateNewKey(Now, Now + TimeSpan.FromDays(90));

        key.ShouldBeSameAs(made);
        inner.Created.ShouldBe(0);
        locks.Taken.ShouldBe(1);
        locks.Released.ShouldBe(1);
    }

    [Fact]
    public void AKeyIsMadeWhenTheRingHasNoneToUse()
    {
        var inner = new FakeKeyManager();

        var key = Manager(inner, defaultKey: null, new FakeLock(grants: true)).CreateNewKey(Now, Now + TimeSpan.FromDays(90));

        key.ShouldBeSameAs(inner.Last);
        inner.Created.ShouldBe(1);
    }

    [Fact]
    public void AKeyAskedForOnPurposeIsMadeEvenWithAnOlderOneInTheRing()
    {
        var inner = new FakeKeyManager();

        Manager(inner, new FakeKey(Now - TimeSpan.FromDays(3)), new FakeLock(grants: true)).CreateNewKey(Now, Now + TimeSpan.FromDays(90));

        inner.Created.ShouldBe(1);
    }

    [Fact]
    public void AKeyIsStillMadeWhenTheLockIsNotFree()
    {
        var inner = new FakeKeyManager();
        var locks = new FakeLock(grants: false);

        Manager(inner, new FakeKey(Now), locks).CreateNewKey(Now, Now + TimeSpan.FromDays(90));

        inner.Created.ShouldBe(1);
        locks.Released.ShouldBe(0);
    }

    private static LockedKeyManager Manager(FakeKeyManager inner, IKey? defaultKey, FakeLock locks) =>
        new(inner, new FakeResolver(defaultKey), locks, NullLogger<LockedKeyManager>.Instance);

    private sealed class FakeKey(DateTimeOffset created) : IKey
    {
        public DateTimeOffset ActivationDate => CreationDate;

        public DateTimeOffset CreationDate { get; } = created;

        public DateTimeOffset ExpirationDate => CreationDate + TimeSpan.FromDays(90);

        public bool IsRevoked => false;

        public Guid KeyId { get; } = Guid.NewGuid();

        public IAuthenticatedEncryptorDescriptor Descriptor => throw new NotSupportedException();

        public IAuthenticatedEncryptor? CreateEncryptor() => throw new NotSupportedException();
    }

    private sealed class FakeKeyManager : IKeyManager
    {
        public int Created { get; private set; }

        public IKey? Last { get; private set; }

        public IKey CreateNewKey(DateTimeOffset activationDate, DateTimeOffset expirationDate)
        {
            Created++;
            return Last = new FakeKey(Now);
        }

        public IReadOnlyCollection<IKey> GetAllKeys() => [];

        public CancellationToken GetCacheExpirationToken() => CancellationToken.None;

        public void RevokeKey(Guid keyId, string? reason = null)
        {
        }

        public void RevokeAllKeys(DateTimeOffset revocationDate, string? reason = null)
        {
        }
    }

    private sealed class FakeResolver(IKey? defaultKey) : IDefaultKeyResolver
    {
        public DefaultKeyResolution ResolveDefaultKeyPolicy(DateTimeOffset now, IEnumerable<IKey> allKeys) =>
            new() { DefaultKey = defaultKey, ShouldGenerateNewKey = defaultKey is null };
    }

    private sealed class FakeLock(bool grants) : IDistributedLock
    {
        public int Taken { get; private set; }

        public int Released { get; private set; }

        public Task<IDistributedLockHandle?> TryAcquireAsync(string name, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (!grants)
            {
                return Task.FromResult<IDistributedLockHandle?>(null);
            }

            Taken++;
            return Task.FromResult<IDistributedLockHandle?>(new Handle(this, name));
        }

        private sealed class Handle(FakeLock owner, string name) : IDistributedLockHandle
        {
            public string Name => name;

            public CancellationToken Lost => CancellationToken.None;

            public ValueTask DisposeAsync()
            {
                owner.Released++;
                return ValueTask.CompletedTask;
            }
        }
    }
}
