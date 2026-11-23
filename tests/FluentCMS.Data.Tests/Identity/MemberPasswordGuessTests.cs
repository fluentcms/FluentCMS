using FluentCMS.Content;
using FluentCMS.Core.Identity.Members;
using FluentCMS.Core.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Identity.IdentityFixture;

namespace FluentCMS.Data.Tests.Identity;

// Wrong passwords of a member sent at once, on /me/change-password and on the login page: each is counted towards the
// lockout, and on /me/change-password no more are checked than its limit, whatever the answer of a right one (#3597).
public sealed class MemberPasswordGuessTests : IDisposable
{
    private const string NewPassword = "Another-horse-2";

    private static readonly int Limit = new MemberOptions().Lockout.MaxFailedAccessAttempts;

    private readonly MemberHasher _hasher = new();
    private readonly LosingSaves _losing = new();
    private readonly IdentityFixture _fixture;

    public MemberPasswordGuessTests() =>
        _fixture = new IdentityFixture(cms =>
        {
            cms.Services.AddSingleton<IPasswordHasher<CmsMember>>(_hasher);
            cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(_losing));
        });

    public void Dispose() => _fixture.Dispose();

    // Wrong current passwords sent at once all read the member before any of them saved. Each is counted before it is
    // checked, one that lost its save on what is stored then, so that they count as many as they are: the one that reaches
    // the limit locks the member out, and those after it are refused unchecked.
    [Fact]
    public async Task WrongCurrentPasswordsSentAtOnceAreEachCountedAndThoseAfterTheLimitUnchecked()
    {
        var member = await CreateMemberAsync();
        var original = (await StoredAsync(member.Id)).PasswordHash!;
        var gate = new Gate(Limit + 2);

        var answers = await Task.WhenAll(Enumerable.Range(0, gate.Count).Select(index => MembersAsync(async members =>
        {
            var mia = (await members.FindByIdAsync(member.Id.ToString("D")))!;
            await gate.PassAsync();
            return (await members.ChangePasswordAsync(mia, $"wrong-{index}", NewPassword)).Errors.ShouldHaveSingleItem().Code;
        }))).WaitAsync(TimeSpan.FromMinutes(1), Token);

        answers.ShouldAllBe(code => code == nameof(IdentityErrorDescriber.PasswordMismatch));
        _hasher.Verified.Count(verified => verified.Hash == original).ShouldBe(Limit);
        (await MembersAsync(async members => await members.IsLockedOutAsync((await members.FindByIdAsync(member.Id.ToString("D")))!))).ShouldBeTrue();
        (await MembersAsync(async members => await members.ChangePasswordAsync((await members.FindByIdAsync(member.Id.ToString("D")))!, Password, NewPassword)))
            .Errors.ShouldHaveSingleItem().Code.ShouldBe(nameof(IdentityErrorDescriber.PasswordMismatch));
        (await MembersAsync(members => members.CheckPasswordAsync(member, Password))).ShouldBeTrue();
    }

    // A right password sent with wrong ones read the member before any of them saved, and loses its save to the first wrong
    // one. It is not answered with that failure, which would tell the sender it was right: it is counted again on what is
    // stored and taken, and every wrong one is answered as wrong.
    [Fact]
    public async Task ARightPasswordThatLostItsSaveToAWrongOneIsTakenOnItsNextTry()
    {
        var member = await CreateMemberAsync();
        var gate = new Gate(Limit);
        Task<string> GuessAsync(string current, Func<Task> after) => MembersAsync(async members =>
        {
            var mia = (await members.FindByIdAsync(member.Id.ToString("D")))!;
            await gate.PassAsync();
            await after().WaitAsync(TimeSpan.FromSeconds(20), Token);
            var result = await members.ChangePasswordAsync(mia, current, NewPassword);
            return result.Succeeded ? "" : result.Errors.ShouldHaveSingleItem().Code;
        });

        List<Task<string>> wrong = [.. Enumerable.Range(0, Limit - 1).Select(index => GuessAsync($"wrong-{index}", () => Task.CompletedTask))];
        var right = GuessAsync(Password, () => Task.WhenAny(wrong));

        (await right.WaitAsync(TimeSpan.FromMinutes(1), Token)).ShouldBe("");
        (await Task.WhenAll(wrong).WaitAsync(TimeSpan.FromMinutes(1), Token)).ShouldAllBe(code => code == nameof(IdentityErrorDescriber.PasswordMismatch));
        (await MembersAsync(async members => await members.CheckPasswordAsync((await members.FindByIdAsync(member.Id.ToString("D")))!, NewPassword))).ShouldBeTrue();
    }

    // On /me/change-password, a right password and a wrong one, whose check waits until the right one has read the member
    // and reached its own check, which waits until the wrong one is answered. The right one's save then loses to the count
    // of the wrong one, and it is answered as right all the same: before, it was answered with the concurrency failure,
    // which no wrong password gets, and which told the sender it was right.
    [Fact]
    public async Task ARightPasswordOfTheMemberApiWhoseSaveLostIsAnsweredAsRight()
    {
        var member = await CreateMemberAsync();
        var checking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _hasher.BeforeCheck = provided =>
        {
            if (provided == Password)
            {
                checking.TrySetResult();
                answered.Task.Wait(TimeSpan.FromSeconds(20), Token);
            }
            else
            {
                checking.Task.Wait(TimeSpan.FromSeconds(20), Token);
            }
        };
        Task<string> ChangeAsync(string current) => _fixture.WithAsync(async services =>
        {
            using var caller = MemberOperation.Begin(services.GetRequiredService<IOperationContextAccessor>(), member, services.GetRequiredService<TimeProvider>());
            try
            {
                await services.GetRequiredService<MemberAccountService>().ChangePasswordAsync(current, NewPassword, Token);
                return "";
            }
            catch (ValidationException refused)
            {
                return refused.Errors.ShouldHaveSingleItem().Code;
            }
        });

        var right = Task.Run(() => ChangeAsync(Password), Token);
        var wrong = Task.Run(() => ChangeAsync("wrong"), Token);
        (await wrong.WaitAsync(TimeSpan.FromMinutes(1), Token)).ShouldBe(MemberErrorCodes.WrongPassword);
        answered.TrySetResult();

        (await right.WaitAsync(TimeSpan.FromMinutes(1), Token)).ShouldBe("");
        _hasher.BeforeCheck = null;
        (await MembersAsync(async members => await members.CheckPasswordAsync((await members.FindByIdAsync(member.Id.ToString("D")))!, NewPassword))).ShouldBeTrue();
    }

    // Saves that keep losing, as under a flood of other saves of the member. A guess whose count is never saved is never
    // checked: it ends with the concurrency failure after the same saves whether it was right or wrong. A guess that is
    // counted is checked, and a wrong one is answered then; a right one checks the password and hashes the new one again on
    // every try of its save, and when they run out it is answered as a wrong one, and stays counted.
    [Fact]
    public async Task AChangeWhoseSavesKeepLosingAnswersTheSameForARightAndAWrongPassword()
    {
        var member = await CreateMemberAsync();
        var tries = 2 * Limit;
        async Task<(string Code, int Saves, int Checks, int Hashes)> ChangeAsync(string current, int passing)
        {
            _hasher.Clear();
            _losing.Arm(passing);
            var result = await MembersAsync(async members => await members.ChangePasswordAsync((await members.FindByIdAsync(member.Id.ToString("D")))!, current, NewPassword));
            return (result.Errors.ShouldHaveSingleItem().Code, _losing.Disarm(), _hasher.Verified.Count, _hasher.Hashed);
        }

        var right = await ChangeAsync(Password, passing: 0);
        var wrong = await ChangeAsync("wrong", passing: 0);
        right.ShouldBe((nameof(IdentityErrorDescriber.ConcurrencyFailure), tries, 0, 0));
        wrong.ShouldBe(right);

        right = await ChangeAsync(Password, passing: 1);
        wrong = await ChangeAsync("wrong", passing: 1);
        right.ShouldBe((nameof(IdentityErrorDescriber.PasswordMismatch), 1 + tries, tries, tries));
        wrong.ShouldBe((nameof(IdentityErrorDescriber.PasswordMismatch), 1, 1, 0));

        var stored = await StoredAsync(member.Id);
        stored.AccessFailedCount.ShouldBe(2);
        (await MembersAsync(members => members.CheckPasswordAsync(stored, Password))).ShouldBeTrue();
    }

    // The login page and the direct login grant check a password before they count it, through Identity's sign-in manager.
    // Wrong ones checked at once all read the member before any of them saved, and each count that lost its save is made
    // again on what is stored, so that they lock the member out as they would one after the other.
    [Fact]
    public async Task WrongPasswordsTheLoginChecksAtOnceAreEachCounted()
    {
        var member = await CreateMemberAsync();
        var gate = new Gate(Limit);

        var answers = await Task.WhenAll(Enumerable.Range(0, gate.Count).Select(index => _fixture.WithAsync(async services =>
        {
            var signIn = services.GetRequiredService<MemberSignInManager>();
            var mia = (await signIn.UserManager.FindByIdAsync(member.Id.ToString("D")))!;
            await gate.PassAsync();
            return await signIn.CheckPasswordSignInAsync(mia, $"wrong-{index}", lockoutOnFailure: true);
        }))).WaitAsync(TimeSpan.FromMinutes(1), Token);

        answers.ShouldAllBe(answer => !answer.Succeeded);
        answers.Count(answer => answer.IsLockedOut).ShouldBeGreaterThanOrEqualTo(1);
        (await MembersAsync(async members => await members.IsLockedOutAsync((await members.FindByIdAsync(member.Id.ToString("D")))!))).ShouldBeTrue();
    }

    private Task<T> MembersAsync<T>(Func<MemberManager, Task<T>> work) =>
        _fixture.WithAsync(services => work(services.GetRequiredService<MemberManager>()));

    private Task<CmsMember> StoredAsync(Guid id) =>
        MembersAsync(async members => (await members.FindByIdAsync(id.ToString("D"))).ShouldNotBeNull());

    private async Task<CmsMember> CreateMemberAsync()
    {
        var member = new CmsMember { Email = "mia@example.com", EmailConfirmed = true };
        await MembersAsync(async members =>
        {
            Succeeded(await members.CreateAsync(member, Password));
            return true;
        });
        _hasher.Clear();
        return member;
    }

    // Identity's hasher, which records each check of a password: the hash it was checked against, and the guess.
    private sealed class MemberHasher : IPasswordHasher<CmsMember>
    {
        private readonly PasswordHasher<CmsMember> _inner = new();
        private int _hashed;

        public System.Collections.Concurrent.ConcurrentQueue<(string Hash, string Provided)> Verified { get; } = new();

        public int Hashed => Volatile.Read(ref _hashed);

        public Action<string>? BeforeCheck { get; set; }

        public void Clear()
        {
            Verified.Clear();
            Interlocked.Exchange(ref _hashed, 0);
        }

        public string HashPassword(CmsMember user, string password)
        {
            Interlocked.Increment(ref _hashed);
            return _inner.HashPassword(user, password);
        }

        public PasswordVerificationResult VerifyHashedPassword(CmsMember user, string hashedPassword, string providedPassword)
        {
            BeforeCheck?.Invoke(providedPassword);
            Verified.Enqueue((hashedPassword, providedPassword));
            return _inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
        }
    }

    // Lets the requests go on only once each has read the member, so that all of them read it as it was before any saved.
    private sealed class Gate(int count)
    {
        private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public int Count => count;

        public Task PassAsync()
        {
            if (Interlocked.Increment(ref _arrived) == count)
            {
                _open.TrySetResult();
            }

            return _open.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
        }
    }

    // While armed, every save of a member loses, as to another request's save that went through just before it, and is
    // counted.
    private sealed class LosingSaves : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        private int _armed;
        private int _passing;
        private int _saves;

        // The first saves, as many as passing, go through.
        public void Arm(int passing = 0)
        {
            Volatile.Write(ref _passing, passing);
            Volatile.Write(ref _armed, 1);
        }

        public int Disarm()
        {
            Volatile.Write(ref _armed, 0);
            return Interlocked.Exchange(ref _saves, 0);
        }

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _armed) == 1)
            {
                foreach (var entry in eventData.Context!.ChangeTracker.Entries<CmsMember>().Where(entry => entry.State == EntityState.Modified))
                {
                    if (Interlocked.Increment(ref _saves) > Volatile.Read(ref _passing))
                    {
                        entry.Property(member => member.ConcurrencyStamp).OriginalValue = "another-request";
                    }
                }
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
