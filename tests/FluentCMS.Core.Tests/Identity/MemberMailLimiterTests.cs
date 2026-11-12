using FluentCMS.Core.Identity.Members;
using FluentCMS.Providers;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Identity;

// Two instances are two limiters over one shared cache and one shared lock, as a host with a distributed cache and the
// database lock has them.
public sealed class MemberMailLimiterTests : IDisposable
{
    private const string Address = "OWNER@EXAMPLE.COM";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly SharedCache _cache = new();
    private readonly SharedLock _lock = new();
    private readonly Clock _time = new();

    public void Dispose() => _lock.Dispose();

    [Fact]
    public async Task TheDefaultInMemoryCacheIsDecidedOnTheInstanceWithoutTheDistributedLock()
    {
        using var limiter = new MemberMailLimiter(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), new NoLock(), _time);

        var granted = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => limiter.TryAcquireAsync(Address, MemberMailSlot.Requested, Token), Token)));

        granted.Count(sent => sent).ShouldBe(1);
    }

    [Fact]
    public async Task TwoInstancesAskingAtOnceSendOneMailToAnAddress()
    {
        using var first = new MemberMailLimiter(_cache, _lock, _time);
        using var second = new MemberMailLimiter(_cache, _lock, _time);

        var asked = Enumerable.Range(0, 40)
            .Select(attempt => Task.Run(() => (attempt % 2 == 0 ? first : second).TryAcquireAsync(Address, MemberMailSlot.Requested, Token), Token));
        var granted = await Task.WhenAll(asked);

        granted.Count(sent => sent).ShouldBe(1);
    }

    [Fact]
    public async Task WhatOneInstanceSentHoldsBackTheOtherForAMinute()
    {
        using var first = new MemberMailLimiter(_cache, _lock, _time);
        using var second = new MemberMailLimiter(_cache, _lock, _time);

        (await first.TryAcquireAsync(Address, MemberMailSlot.Notice, Token)).ShouldBeTrue();
        (await second.TryAcquireAsync(Address, MemberMailSlot.Notice, Token)).ShouldBeFalse();

        _time.Advance(MemberMailLimiter.Window - TimeSpan.FromSeconds(1));
        (await second.TryAcquireAsync(Address, MemberMailSlot.Notice, Token)).ShouldBeFalse();

        _time.Advance(TimeSpan.FromSeconds(2));
        (await second.TryAcquireAsync(Address, MemberMailSlot.Notice, Token)).ShouldBeTrue();
        (await first.TryAcquireAsync(Address, MemberMailSlot.Notice, Token)).ShouldBeFalse();
    }

    [Fact]
    public async Task ANoticeDoesNotHoldBackWhatTheOwnerAskedForAndAnotherAddressIsApart()
    {
        using var limiter = new MemberMailLimiter(_cache, _lock, _time);

        (await limiter.TryAcquireAsync(Address, MemberMailSlot.Notice, Token)).ShouldBeTrue();
        (await limiter.TryAcquireAsync(Address, MemberMailSlot.Requested, Token)).ShouldBeTrue();
        (await limiter.TryAcquireAsync(Address, MemberMailSlot.Requested, Token)).ShouldBeFalse();
        (await limiter.TryAcquireAsync("OTHER@EXAMPLE.COM", MemberMailSlot.Requested, Token)).ShouldBeTrue();
    }

    [Fact]
    public async Task TheCacheHoldsAHashOfTheAddressAndNoMailGoesWithoutTheLock()
    {
        using var limiter = new MemberMailLimiter(_cache, _lock, _time);
        (await limiter.TryAcquireAsync(Address, MemberMailSlot.Requested, Token)).ShouldBeTrue();
        (await _cache.GetAsync(Address, Token)).ShouldBeNull();

        using var unavailable = new MemberMailLimiter(_cache, new NoLock(), _time);
        (await unavailable.TryAcquireAsync("NEW@EXAMPLE.COM", MemberMailSlot.Requested, Token)).ShouldBeFalse();
    }

    private sealed class Clock : TimeProvider
    {
        private long _ticks = DateTimeOffset.UtcNow.UtcTicks;

        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

        public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
    }

    // A cache every limiter of the test shares, as a host's Redis is; not the in-memory default, which a limiter decides alone.
    private sealed class SharedCache : IDistributedCache
    {
        private readonly MemoryDistributedCache _inner = new(Options.Create(new MemoryDistributedCacheOptions()));

        public byte[]? Get(string key) => _inner.Get(key);

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => _inner.GetAsync(key, token);

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _inner.Set(key, value, options);

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => _inner.SetAsync(key, value, options, token);

        public void Refresh(string key) => _inner.Refresh(key);

        public Task RefreshAsync(string key, CancellationToken token = default) => _inner.RefreshAsync(key, token);

        public void Remove(string key) => _inner.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default) => _inner.RemoveAsync(key, token);
    }

    // One lock for every limiter of the test, as cms_lock is one for every instance.
    private sealed class SharedLock : IDistributedLock, IDisposable
    {
        private readonly SemaphoreSlim _gate = new(1, 1);

        public void Dispose() => _gate.Dispose();

        public async Task<IDistributedLockHandle?> TryAcquireAsync(string name, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            await _gate.WaitAsync(timeout, cancellationToken) ? new Handle(name, _gate) : null;

        private sealed class Handle(string name, SemaphoreSlim gate) : IDistributedLockHandle
        {
            public string Name => name;

            public CancellationToken Lost => CancellationToken.None;

            public ValueTask DisposeAsync()
            {
                gate.Release();
                return ValueTask.CompletedTask;
            }
        }
    }

    // A lock that another holder keeps for longer than the limiter waits.
    private sealed class NoLock : IDistributedLock
    {
        public Task<IDistributedLockHandle?> TryAcquireAsync(string name, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            Task.FromResult<IDistributedLockHandle?>(null);
    }
}
