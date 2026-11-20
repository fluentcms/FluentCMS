using FluentCMS.Core.DependencyInjection;

namespace FluentCMS.Core.Content;

// FluentCms:Content. The bounds of a write that no request limit gives a call of the SDK or an import.
public sealed class ContentWriteOptions
{
    public const string Section = "Content";

    public const int DefaultMaxItems = 1000;

    // The most items of a multiple field or of blocks that declares no maximum of its own; one that does, higher or lower,
    // is held to its own.
    public int MaxItems { get; set; } = DefaultMaxItems;

    // How long one hook may run inside the transaction of the write. One that runs longer has its token cancelled and fails
    // the write, which rolls back whole.
    public TimeSpan HookTimeout { get; set; } = TimeSpan.FromSeconds(30);

    // A hook that ends after at least this long is logged as slow, by name.
    public TimeSpan SlowHookThreshold { get; set; } = TimeSpan.FromSeconds(1);

    internal void Validate()
    {
        Require(MaxItems > 0, $"{nameof(MaxItems)} is {MaxItems}; it must be positive.");
        Require(HookTimeout > TimeSpan.Zero, $"{nameof(HookTimeout)} is {HookTimeout}; it must be positive.");
        Require(SlowHookThreshold > TimeSpan.Zero, $"{nameof(SlowHookThreshold)} is {SlowHookThreshold}; it must be positive.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new FluentCmsConfigurationException(HostErrorCodes.InvalidOption, message);
        }
    }
}
