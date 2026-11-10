using FluentCMS.Core.DependencyInjection;

namespace FluentCMS.Core.Identity.ApiKeys;

// cms.ApiKeys, and FluentCms:ApiKeys in configuration.
public sealed class ApiKeyOptions
{
    internal const string Section = "ApiKeys";

    internal static readonly TimeSpan MaxRotationOverlap = TimeSpan.FromDays(30);

    // How long the secret a rotation replaces is still accepted beside the new one. Zero ends it at the rotation.
    public TimeSpan RotationOverlap { get; set; } = TimeSpan.FromHours(24);

    internal void Validate()
    {
        if (RotationOverlap < TimeSpan.Zero || RotationOverlap > MaxRotationOverlap)
        {
            throw new FluentCmsConfigurationException(
                IdentityErrorCodes.InvalidOptions,
                $"{nameof(RotationOverlap)} is {RotationOverlap}; it must be from 00:00:00 to {MaxRotationOverlap.Days} days.");
        }
    }
}
