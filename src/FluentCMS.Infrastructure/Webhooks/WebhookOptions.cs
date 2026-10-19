using Microsoft.Extensions.Options;

namespace FluentCMS.Infrastructure.Webhooks;

// Webhooks__AdminUrl=https://cms.example.com/admin makes the links in a delivery absolute; without it they are paths in the admin.
public sealed class WebhookOptions
{
    public const string SectionName = "Webhooks";

    public string? AdminUrl { get; set; }

    // How long an endpoint has to answer one delivery.
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    // Attempts per delivery, retried with the job queue's growing delay.
    public int MaxAttempts { get; set; } = 6;
}

internal sealed class WebhookOptionsValidation : IValidateOptions<WebhookOptions>
{
    public ValidateOptionsResult Validate(string? name, WebhookOptions options)
    {
        var errors = new List<string>();
        if (options.Timeout <= TimeSpan.Zero || options.Timeout > TimeSpan.FromMinutes(5))
        {
            errors.Add($"Webhooks__Timeout is {options.Timeout}; it must be longer than zero and at most five minutes.");
        }

        if (options.MaxAttempts is < 1 or > 20)
        {
            errors.Add($"Webhooks__MaxAttempts is {options.MaxAttempts}; it must be from 1 to 20.");
        }

        if (options.AdminUrl is { Length: > 0 } url && !(Uri.TryCreate(url, UriKind.Absolute, out var parsed) && parsed.Scheme is "https" or "http"))
        {
            errors.Add($"Webhooks__AdminUrl is '{url}'; it must be the admin's full address, such as https://cms.example.com/admin.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
