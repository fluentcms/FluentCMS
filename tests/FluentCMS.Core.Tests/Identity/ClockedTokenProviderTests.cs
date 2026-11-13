using FluentCMS.Core.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Identity;

public sealed class ClockedTokenProviderTests
{
    private const string Name = "TestTokens";
    private const string Purpose = "ResetPassword";

    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    private readonly EphemeralDataProtectionProvider _ring = new();
    private readonly Clock _time = new(new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.Zero));
    private readonly User _user = new("7f0c2d1e", "stamp-1");

    [Fact]
    public async Task ATokenIsGoodUntilTheEndOfItsLifetimeOnTheClockAndNotATickLater()
    {
        using var users = Users();
        var provider = Provider(_time);
        var token = await provider.GenerateAsync(Purpose, users, _user);

        _time.Advance(Lifetime);
        (await provider.ValidateAsync(Purpose, token, users, _user)).ShouldBeTrue();

        _time.Advance(TimeSpan.FromTicks(1));
        (await provider.ValidateAsync(Purpose, token, users, _user)).ShouldBeFalse();
    }

    [Fact]
    public async Task ATokenOfAnotherPurposeUserOrStampOrNoTokenIsRefused()
    {
        using var users = Users();
        var provider = Provider(_time);
        var token = await provider.GenerateAsync(Purpose, users, _user);

        (await provider.ValidateAsync("EmailConfirmation", token, users, _user)).ShouldBeFalse();
        (await provider.ValidateAsync(Purpose, token, users, _user with { Id = "another" })).ShouldBeFalse();
        (await provider.ValidateAsync(Purpose, token, users, _user with { Stamp = "stamp-2" })).ShouldBeFalse();
        (await provider.ValidateAsync(Purpose, "not a token", users, _user)).ShouldBeFalse();
        (await provider.ValidateAsync(Purpose, Convert.ToBase64String(_ring.CreateProtector(Name).Protect([1, 2, 3])), users, _user)).ShouldBeFalse();
    }

    // A link mailed before the upgrade still works, and Identity's own provider would read a token of this one. Identity's
    // reads the system clock, so this one's is far from it: years before it, a token Identity's made is not yet past its
    // lifetime here, and years after it, one made here is not yet past it there.
    [Fact]
    public async Task ATokenIsInTheFormatOfIdentitysProviderBothWays()
    {
        using var users = Users();
        var identity = new DataProtectorTokenProvider<User>(_ring, TokenOptions(), NullLogger<DataProtectorTokenProvider<User>>.Instance);

        var before = Provider(new Clock(new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        (await before.ValidateAsync(Purpose, await identity.GenerateAsync(Purpose, users, _user), users, _user)).ShouldBeTrue();

        var after = Provider(new Clock(new DateTimeOffset(9000, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        (await after.ValidateAsync(Purpose, await identity.GenerateAsync(Purpose, users, _user), users, _user)).ShouldBeFalse();
        (await identity.ValidateAsync(Purpose, await after.GenerateAsync(Purpose, users, _user), users, _user)).ShouldBeTrue();
    }

    private TestProvider Provider(TimeProvider time) => new(_ring, TokenOptions(), time);

    private static IOptions<DataProtectionTokenProviderOptions> TokenOptions() =>
        Options.Create(new DataProtectionTokenProviderOptions { Name = Name, TokenLifespan = Lifetime });

    private static UserManager<User> Users() =>
        new(new UserStore(), null!, null!, null!, null!, null!, null!, null!, NullLogger<UserManager<User>>.Instance);

    private sealed class TestProvider(IDataProtectionProvider ring, IOptions<DataProtectionTokenProviderOptions> options, TimeProvider time)
        : ClockedTokenProvider<User>(ring, options, time, NullLogger<DataProtectorTokenProvider<User>>.Instance);

    private sealed record User(string Id, string Stamp);

    // Only what the token providers read: the id and the security stamp.
    private sealed class UserStore : IUserSecurityStampStore<User>
    {
        public Task<string> GetUserIdAsync(User user, CancellationToken cancellationToken) => Task.FromResult(user.Id);

        public Task<string?> GetSecurityStampAsync(User user, CancellationToken cancellationToken) => Task.FromResult<string?>(user.Stamp);

        public Task SetSecurityStampAsync(User user, string stamp, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string?> GetUserNameAsync(User user, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SetUserNameAsync(User user, string? userName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string?> GetNormalizedUserNameAsync(User user, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SetNormalizedUserNameAsync(User user, string? normalizedName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IdentityResult> CreateAsync(User user, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IdentityResult> UpdateAsync(User user, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IdentityResult> DeleteAsync(User user, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<User?> FindByIdAsync(string userId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<User?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    private sealed class Clock(DateTimeOffset start) : TimeProvider
    {
        private long _ticks = start.UtcTicks;

        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

        public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
    }
}
