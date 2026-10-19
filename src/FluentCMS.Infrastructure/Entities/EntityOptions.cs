using Microsoft.Extensions.Options;

namespace FluentCMS.Infrastructure.Entities;

public sealed class EntityOptions
{
    public const string SectionName = "Entities";

    public const int MaxVerifyWithinDays = 36500;

    // A fact last verified longer ago than this is stale (ENT-03).
    public TimeSpan VerifyWithin { get; set; } = TimeSpan.FromDays(180);
}

internal sealed class EntityOptionsValidation : IValidateOptions<EntityOptions>
{
    public ValidateOptionsResult Validate(string? name, EntityOptions options)
    {
        return options.VerifyWithin > TimeSpan.Zero && options.VerifyWithin <= TimeSpan.FromDays(EntityOptions.MaxVerifyWithinDays)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"Entities__VerifyWithin is {options.VerifyWithin}; it must be longer than zero and at most {EntityOptions.MaxVerifyWithinDays} days.");
    }
}
