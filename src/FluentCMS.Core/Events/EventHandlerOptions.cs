using FluentCMS.Core.DependencyInjection;

namespace FluentCMS.Core.Events;

// FluentCms:Events:Handlers. The in-process handlers run after the commit while the caller of the write or the publish waits.
public sealed class EventHandlerOptions
{
    public const string Section = "Events:Handlers";

    // How long the caller waits for one handler. A handler that takes longer has its token cancelled and is left to end on
    // its own, as the handlers of a job event are after OutboxOptions.JobEventTimeout.
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    // A handler that ends after at least this long is logged as slow.
    public TimeSpan SlowThreshold { get; set; } = TimeSpan.FromSeconds(1);

    internal void Validate()
    {
        Require(Timeout > TimeSpan.Zero, $"{nameof(Timeout)} is {Timeout}; it must be positive.");
        Require(SlowThreshold > TimeSpan.Zero, $"{nameof(SlowThreshold)} is {SlowThreshold}; it must be positive.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new FluentCmsConfigurationException(HostErrorCodes.InvalidOption, message);
        }
    }
}
