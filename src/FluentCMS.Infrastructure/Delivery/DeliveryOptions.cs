using Microsoft.Extensions.Options;

namespace FluentCMS.Infrastructure.Delivery;

public sealed class DeliveryOptions
{
    public const string SectionName = "Delivery";

    // How long a preview token works unless it is revoked or its change set ends first (API-04).
    public TimeSpan PreviewTokenLifetime { get; set; } = TimeSpan.FromDays(7);
}

internal sealed class DeliveryOptionsValidation : IValidateOptions<DeliveryOptions>
{
    public ValidateOptionsResult Validate(string? name, DeliveryOptions options)
    {
        return options.PreviewTokenLifetime > TimeSpan.Zero && options.PreviewTokenLifetime <= TimeSpan.FromDays(365)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"Delivery__PreviewTokenLifetime is {options.PreviewTokenLifetime}; it must be longer than zero and at most 365 days.");
    }
}
