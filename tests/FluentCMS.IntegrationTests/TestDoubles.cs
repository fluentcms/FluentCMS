using FluentCMS.Core.Identity;

namespace FluentCMS.IntegrationTests;

// Stands in for the host's ICurrentInitiator, which Infrastructure expects the host to register.
internal sealed class TestInitiator(Initiator? current) : ICurrentInitiator
{
    public Initiator? Current { get; } = current;
}

internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow()
    {
        return now;
    }
}
