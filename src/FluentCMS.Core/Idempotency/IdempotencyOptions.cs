using FluentCMS.Core.DependencyInjection;

namespace FluentCMS.Core.Idempotency;

// cms.Idempotency, and FluentCms:Idempotency in configuration.
public sealed class IdempotencyOptions
{
    internal const string Section = "Idempotency";

    internal static readonly TimeSpan MaxWindow = TimeSpan.FromDays(30);

    // How long a key and the response it stored are kept, from the first request that used it.
    public TimeSpan Window { get; set; } = TimeSpan.FromHours(24);

    // How long a request that holds a key may run before a repeat of it may take the key over, for one whose instance
    // stopped before it answered.
    public TimeSpan InProgressTimeout { get; set; } = TimeSpan.FromMinutes(5);

    internal void Validate()
    {
        Require(Window > TimeSpan.Zero && Window <= MaxWindow, $"{nameof(Window)} is {Window}; it must be more than 00:00:00 and at most {MaxWindow.Days} days.");
        Require(InProgressTimeout > TimeSpan.Zero && InProgressTimeout <= Window, $"{nameof(InProgressTimeout)} is {InProgressTimeout}; it must be more than 00:00:00 and at most {nameof(Window)}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new FluentCmsConfigurationException(HostErrorCodes.InvalidOption, message);
        }
    }
}
