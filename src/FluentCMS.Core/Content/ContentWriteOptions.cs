using FluentCMS.Core.DependencyInjection;

namespace FluentCMS.Core.Content;

// FluentCms:Content. The bounds of a write that no request limit gives a call of the SDK or an import.
public sealed class ContentWriteOptions
{
    public const string Section = "Content";

    public const int DefaultMaxItems = 1000;

    // A day holds the write's transaction far longer than any hook should, and stays well inside what a timer takes.
    internal static readonly TimeSpan MaxHookTimeout = TimeSpan.FromDays(1);

    // The most items of a multiple field or of blocks that declares no maximum of its own; one that does, higher or lower,
    // is held to its own.
    public int MaxItems { get; set; } = DefaultMaxItems;

    // How long one hook may run inside the transaction of the write. One that runs longer has its token cancelled and fails
    // the write, which rolls back whole.
    public TimeSpan HookTimeout { get; set; } = TimeSpan.FromSeconds(30);

    // A hook that ends after at least this long is logged as slow, by name.
    public TimeSpan SlowHookThreshold { get; set; } = TimeSpan.FromSeconds(1);

    // How long a write in a transaction of its own waits for what another transaction holds, the entry's rows or on SQLite
    // the database, before it gives up with FCMS2411. A third of the drivers' 30 second command timeout, so that the lock
    // wait and not the command is what ends a wait, with room for the statement itself, and ten times the threshold of a
    // slow hook, so that only a holder far slower than a write should be is given up on.
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(10);

    internal void Validate()
    {
        Require(MaxItems > 0, $"{nameof(MaxItems)} is {MaxItems}; it must be positive.");
        Require(HookTimeout > TimeSpan.Zero && HookTimeout <= MaxHookTimeout, $"{nameof(HookTimeout)} is {HookTimeout}; it must be more than 00:00:00 and at most {MaxHookTimeout.Days} day.");
        Require(LockTimeout > TimeSpan.Zero && LockTimeout <= MaxHookTimeout, $"{nameof(LockTimeout)} is {LockTimeout}; it must be more than 00:00:00 and at most {MaxHookTimeout.Days} day.");
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
