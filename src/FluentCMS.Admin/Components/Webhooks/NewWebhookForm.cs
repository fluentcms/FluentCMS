using FluentCMS.Core.Results;
using FluentCMS.Core.Webhooks;
using Microsoft.Extensions.Localization;

namespace FluentCMS.Admin.Components.Webhooks;

// A new subscription as the screen asks for it, bound from the posted form.
public sealed class NewWebhookForm
{
    public string? Url { get; set; }

    public bool Published { get; set; }

    public bool Reverted { get; set; }

    // Only for the first render: a posted form starts from nothing, so a box left unticked stays unticked.
    public static NewWebhookForm Defaults() => new() { Published = true, Reverted = true };

    public NewWebhookSubscription ToSubscription()
    {
        var events = new List<string>();
        if (Published)
        {
            events.Add(WebhookEvents.ChangeSetPublished);
        }

        if (Reverted)
        {
            events.Add(WebhookEvents.ChangeSetReverted);
        }

        return new NewWebhookSubscription(Url ?? string.Empty, events);
    }
}

// What is wrong with a new subscription, by field, plus anything that belongs to no field.
public sealed class NewWebhookErrors
{
    public const string Events = "Events";

    private readonly Dictionary<string, string> _fields = [];

    public string? Form { get; private set; }

    public string? For(string field) => _fields.GetValueOrDefault(field);

    // Core reports problems by code: the code is the resource key, and Core's own message the text until a resource exists.
    public void Report(IEnumerable<Error> errors, IStringLocalizer localizer)
    {
        foreach (var error in errors)
        {
            var localized = localizer[error.Code];
            var message = localized.ResourceNotFound ? error.Message : localized.Value;
            var field = error.Code switch
            {
                WebhookErrorCodes.UrlInvalid
                    or WebhookErrorCodes.UrlTooLong
                    or WebhookErrorCodes.UrlHasCredentials
                    or WebhookErrorCodes.UrlNotHttps
                    or WebhookErrorCodes.HostNotAllowed => nameof(NewWebhookForm.Url),
                WebhookErrorCodes.EventsMissing or WebhookErrorCodes.EventUnknown => Events,
                _ => null,
            };

            if (field is null)
            {
                Form = Form is null ? message : Form + " " + message;
            }
            else
            {
                _fields[field] = _fields.TryGetValue(field, out var existing) ? existing + " " + message : message;
            }
        }
    }
}
