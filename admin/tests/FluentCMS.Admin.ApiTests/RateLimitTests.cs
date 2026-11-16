using FluentCMS.Admin.Api;
using FluentCMS.Core.Authorization;
using FluentCMS.Testing;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.ApiTests;

// The server's token bucket runs on the test host's clock. With two requests a minute, the third is refused with a
// Retry-After of 30 seconds, and only moving the clock refills the bucket.
public sealed class RateLimitTests
{
    private static CancellationToken Token => AdminHost.Token;

    [Fact]
    public async Task A429WaitsItsRetryAfterOnceAndTheRetryIsLetThrough()
    {
        await using var admin = new AdminHost(managementLimit: 2);
        var time = new WaitingTime(admin.Host.Time, advances: true);
        using var client = await admin.SignInAsync(BuiltInRoles.SuperAdminName, time);

        for (var call = 0; call < 3; call++)
        {
            (await client.Client.GetMeAsync(Token)).Kind.ShouldBe("adminUser");
        }

        time.Waits.ShouldBe([TimeSpan.FromSeconds(30)]);
        client.Counts["GET /api/management/v1/me"].ShouldBe(4);
        client.Notices.ShouldBeEmpty();
    }

    [Fact]
    public async Task ASecond429IsShownAndSurfacesAsAProblem()
    {
        await using var admin = new AdminHost(managementLimit: 2);
        var time = new WaitingTime(admin.Host.Time, advances: false);
        using var client = await admin.SignInAsync(BuiltInRoles.SuperAdminName, time);
        await client.Client.GetMeAsync(Token);
        await client.Client.GetMeAsync(Token);

        var thrown = await Should.ThrowAsync<ApiProblemException>(() => client.Client.GetMeAsync(Token));

        thrown.Problem.Status.ShouldBe(429);
        thrown.Problem.Code.ShouldBe("FCMS3110");
        time.Waits.ShouldBe([TimeSpan.FromSeconds(30)]);
        client.Counts["GET /api/management/v1/me"].ShouldBe(4);
        client.Notices.ShouldBe([TimeSpan.FromSeconds(30)]);
    }

    // The client's wait, recorded and passed at once; when it advances, the host's clock moves by the wait as real time would.
    private sealed class WaitingTime(ControllableTimeProvider host, bool advances) : TimeProvider
    {
        public List<TimeSpan> Waits { get; } = [];

        public override DateTimeOffset GetUtcNow() => host.GetUtcNow();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Waits.Add(dueTime);
            if (advances)
            {
                host.Advance(dueTime);
            }

            return new PassedTimer(callback, state);
        }

        private sealed class PassedTimer : ITimer
        {
            public PassedTimer(TimerCallback callback, object? state) => ThreadPool.QueueUserWorkItem(_ => callback(state));

            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
