namespace FluentCMS.Core.IntegrationTests.Hosting;

// A clock whose time of day stands still, for a host whose rate limits a test exhausts: a bucket then never refills between
// the test's requests, however slow the machine. Timers and elapsed time stay real.
internal sealed class FrozenTime : TimeProvider
{
    private readonly DateTimeOffset _now = TimeProvider.System.GetUtcNow();

    public override DateTimeOffset GetUtcNow() => _now;
}
