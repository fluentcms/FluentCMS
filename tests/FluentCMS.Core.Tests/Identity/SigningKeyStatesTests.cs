using FluentCMS.Core.Identity.Oidc;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Identity;

public sealed class SigningKeyStatesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AKeyIsUpcomingThenActiveThenRetiringThenRetired()
    {
        var first = Key(1, Now.AddDays(-100), retiredAt: Now.AddDays(10));
        var second = Key(2, Now.AddDays(-5), retiredAt: Now.AddDays(100));
        var third = Key(3, Now.AddDays(1));
        SigningKey[] keys = [third, first, second];

        States(keys, Now.AddDays(-50)).ShouldBe([SigningKeyState.Upcoming, SigningKeyState.Active, SigningKeyState.Upcoming]);
        States(keys, Now).ShouldBe([SigningKeyState.Upcoming, SigningKeyState.Retiring, SigningKeyState.Active]);
        States(keys, Now.AddDays(1)).ShouldBe([SigningKeyState.Active, SigningKeyState.Retiring, SigningKeyState.Retiring]);
        States(keys, Now.AddDays(10)).ShouldBe([SigningKeyState.Active, SigningKeyState.Retired, SigningKeyState.Retiring]);
        States(keys, Now.AddDays(100)).ShouldBe([SigningKeyState.Active, SigningKeyState.Retired, SigningKeyState.Retired]);
    }

    [Fact]
    public void TheActiveKeyIsTheOneWithTheHighestSequenceWhoseTimeHasComeAndThatHasNotRetired()
    {
        var revoked = Key(4, Now.AddDays(-1), retiredAt: Now.AddMinutes(-1));
        var active = Key(2, Now.AddDays(-3));
        SigningKey[] keys = [Key(1, Now.AddDays(-9)), revoked, active, Key(5, Now.AddSeconds(1))];

        SigningKeyStates.ActiveOf(keys, Now).ShouldBe(active);
        SigningKeyStates.ActiveOf([revoked], Now).ShouldBeNull();
        SigningKeyStates.ActiveOf([], Now).ShouldBeNull();
    }

    private static SigningKeyState[] States(SigningKey[] keys, DateTimeOffset now)
    {
        var active = SigningKeyStates.ActiveOf(keys, now);
        return [.. keys.Select(key => SigningKeyStates.StateOf(key, active, now))];
    }

    private static SigningKey Key(int sequence, DateTimeOffset activeFrom, DateTimeOffset? retiredAt = null) =>
        new() { Id = Guid.NewGuid(), Use = SigningKey.Signing, Sequence = sequence, CreatedAt = activeFrom, ActiveFrom = activeFrom, RetiredAt = retiredAt };
}
