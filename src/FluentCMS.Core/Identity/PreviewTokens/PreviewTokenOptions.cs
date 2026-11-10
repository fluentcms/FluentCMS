using FluentCMS.Core.DependencyInjection;

namespace FluentCMS.Core.Identity.PreviewTokens;

// cms.PreviewTokens, and FluentCms:PreviewTokens in configuration.
public sealed class PreviewTokenOptions
{
    internal const string Section = "PreviewTokens";

    internal static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(1);

    // What a token lasts when the request names no minutes.
    public TimeSpan DefaultLifetime { get; set; } = TimeSpan.FromMinutes(10);

    // The most a request may ask for. A token cannot be revoked, so this bounds how long a leaked one reads its draft.
    public TimeSpan MaximumLifetime { get; set; } = TimeSpan.FromHours(1);

    internal void Validate()
    {
        // A request asks for whole minutes, so the options are whole minutes too.
        if (DefaultLifetime.Ticks % TimeSpan.TicksPerMinute != 0 || MaximumLifetime.Ticks % TimeSpan.TicksPerMinute != 0)
        {
            throw new FluentCmsConfigurationException(
                IdentityErrorCodes.InvalidOptions,
                $"{nameof(DefaultLifetime)} is {DefaultLifetime} and {nameof(MaximumLifetime)} is {MaximumLifetime}; both must be whole minutes.");
        }

        if (MaximumLifetime < TimeSpan.FromMinutes(1) || MaximumLifetime > MaxLifetime)
        {
            throw new FluentCmsConfigurationException(
                IdentityErrorCodes.InvalidOptions,
                $"{nameof(MaximumLifetime)} is {MaximumLifetime}; it must be from 00:01:00 to {MaxLifetime.Days} day.");
        }

        if (DefaultLifetime < TimeSpan.FromMinutes(1) || DefaultLifetime > MaximumLifetime)
        {
            throw new FluentCmsConfigurationException(
                IdentityErrorCodes.InvalidOptions,
                $"{nameof(DefaultLifetime)} is {DefaultLifetime}; it must be from 00:01:00 to {nameof(MaximumLifetime)}.");
        }
    }
}
