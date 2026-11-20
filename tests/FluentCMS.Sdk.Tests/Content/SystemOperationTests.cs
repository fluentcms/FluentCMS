using FluentCMS.Content;
using FluentCMS.Events;
using FluentCMS.Sites;
using Shouldly;
using Xunit;

namespace FluentCMS.Sdk.Tests.Content;

public class SystemOperationTests
{
    private static readonly DateTimeOffset Then = new(2001, 2, 3, 4, 5, 6, TimeSpan.Zero);
    private static readonly SiteId Site = new(Guid.Parse("00000000-0000-0000-0000-0000000000a1"));

    // A host that freezes or offsets its clock sees that time in the operations it begins, not the time of the machine.
    [Fact]
    public void TheOperationIsStampedByTheClockOfTheHost()
    {
        var accessor = new Accessor();

        using (SystemOperation.Begin(accessor, new Clock(Then), Origin.Job, Site, "fr", "corr-1"))
        {
            accessor.Current!.Timestamp.ShouldBe(Then);
            accessor.Current.Principal.ShouldBe(Principal.System);
            accessor.Current.CorrelationId.ShouldBe("corr-1");
        }

        accessor.Current.ShouldBeNull();
        Should.Throw<ArgumentNullException>(() => SystemOperation.Begin(accessor, null!, Origin.Job, Site));
    }

    // The bus stamps an event with no source by the clock of the host when it is published; until then no clock is read.
    [Fact]
    public void AnEventNotYetPublishedReadsNoClock()
    {
        var ping = new Ping("x");

        ping.Source.OccurredAt.ShouldBe(DateTimeOffset.MinValue);
        ping.Source.ActorKind.ShouldBe(PrincipalKind.System);
        ping.ShouldBe(ping with { });
    }

    private sealed record Ping(string Text) : CmsEvent;

    private sealed class Accessor : IOperationContextAccessor
    {
        public OperationContext? Current { get; set; }
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
